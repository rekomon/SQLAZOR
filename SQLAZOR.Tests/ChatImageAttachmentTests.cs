using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class ChatImageAttachmentTests
{
    private static readonly AiProviderConfig OllamaConfig = new()
    {
        Provider = AiProviderCatalog.Ollama,
        Endpoint = "http://localhost:11434",
        Model = "llava:latest"
    };

    private static readonly AiProviderConfig DeepSeekConfig = new()
    {
        Provider = AiProviderCatalog.DeepSeek,
        Endpoint = "https://api.deepseek.com",
        Model = "deepseek-chat",
        ApiKey = "test-key"
    };

    [Fact]
    public async Task Ollama_sends_images_array_and_omits_it_for_text_only_messages()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new { message = new { content = "I see a diagram" } }));

        await new OllamaService(new HttpClient(handler)).ChatAsync(OllamaConfig, "schema",
        [
            new ChatMessage { Role = "user", Content = "first message" },
            new ChatMessage
            {
                Role = "user",
                Content = "what is in this image?",
                Attachments = [ImageAttachment("er.png", "image/png", [1, 2, 3])]
            }
        ]);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        Assert.False(messages[1].TryGetProperty("images", out _));
        var images = messages[2].GetProperty("images");
        Assert.Equal(1, images.GetArrayLength());
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), images[0].GetString());
    }

    [Fact]
    public async Task DeepSeek_rejects_images_before_any_request()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new { choices = Array.Empty<object>() }));
        var history = new List<ChatMessage>
        {
            new()
            {
                Role = "user",
                Content = "describe this",
                Attachments = [ImageAttachment("screen.png", "image/png", [9, 9])]
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeepSeekService(new HttpClient(handler)).ChatAsync(DeepSeekConfig, "schema", history));

        Assert.Contains("does not accept images", ex.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task DeepSeek_streaming_also_rejects_images()
    {
        var handler = new RecordingHandler(_ => Sse("data: [DONE]\n\n"));
        var service = new DeepSeekService(new HttpClient(handler));
        var history = new List<ChatMessage>
        {
            new()
            {
                Role = "user",
                Content = "describe this",
                Attachments = [ImageAttachment("screen.png", "image/png", [9, 9])]
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.ChatStreamAsync(DeepSeekConfig, "schema", history))
            {
            }
        });

        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public void Zip_export_writes_real_image_bytes()
    {
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var session = new ChatSession
        {
            Title = "Image chat",
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = "see attached",
                    Attachments = [ImageAttachment("diagram.png", "image/png", bytes)]
                }
            ]
        };

        using var archive = new ZipArchive(new MemoryStream(ChatExporter.BuildZip(session)), ZipArchiveMode.Read);
        var entry = archive.GetEntry("attachments/diagram.png");
        Assert.NotNull(entry);

        using var stream = entry!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Assert.Equal(bytes, buffer.ToArray());
    }

    [Fact]
    public void Html_export_embeds_image_as_data_url()
    {
        var session = new ChatSession
        {
            Title = "Image chat",
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = "see attached",
                    Attachments = [ImageAttachment("diagram.png", "image/png", [1, 2])]
                }
            ]
        };

        var html = ChatExporter.ToHtml(session);

        Assert.Contains($"src=\"data:image/png;base64,{Convert.ToBase64String([1, 2])}\"", html);
    }

    [Fact]
    public void Prompt_expansion_references_images_without_inlining_base64()
    {
        var message = new ChatMessage
        {
            Role = "user",
            Content = "describe it",
            Attachments = [ImageAttachment("diagram.png", "image/png", [1, 2, 3])]
        };

        var prompt = ChatContentParser.ExpandForPrompt(message);

        Assert.Contains("[Attached image: diagram.png]", prompt);
        Assert.DoesNotContain(Convert.ToBase64String([1, 2, 3]), prompt);
    }

    [Fact]
    public void Json_roundtrip_preserves_image_mime_type()
    {
        var session = new ChatSession
        {
            Title = "Roundtrip",
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content = "img",
                    Attachments = [ImageAttachment("a.png", "image/png", [5, 6])]
                }
            ]
        };

        var restored = ChatExporter.FromJson(ChatExporter.ToJson(session));

        Assert.NotNull(restored);
        var attachment = Assert.Single(restored!.Messages[0].Attachments);
        Assert.True(attachment.IsImage);
        Assert.Equal("image/png", attachment.MimeType);
        Assert.Equal(Convert.ToBase64String([5, 6]), attachment.Content);
    }

    private static ChatAttachment ImageAttachment(string name, string mime, byte[] bytes) => new()
    {
        FileName = name,
        Content = Convert.ToBase64String(bytes),
        SizeBytes = bytes.Length,
        MimeType = mime
    };

    private static HttpResponseMessage JsonResponse(object payload) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Sse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public string? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responder(request);
        }
    }
}
