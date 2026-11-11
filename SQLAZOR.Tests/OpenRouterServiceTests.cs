using System.Net;
using System.Text;
using System.Text.Json;
using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class OpenRouterServiceTests
{
    private static readonly AiProviderConfig Config = new()
    {
        Provider = AiProviderCatalog.OpenRouter,
        Endpoint = "https://openrouter.ai/api/v1",
        Model = "nvidia/nemotron-3-ultra-550b-a55b:free",
        ApiKey = "sk-or-test"
    };

    [Fact]
    public void Catalog_offers_only_free_models_and_lists_the_default()
    {
        var provider = AiProviderCatalog.Get(AiProviderCatalog.OpenRouter);

        Assert.True(provider.RequiresApiKey);
        Assert.True(provider.SupportsImages);
        Assert.Equal("https://openrouter.ai/api/v1", provider.DefaultEndpoint);
        // The auto-router only targets free endpoints; every other entry carries the :free suffix.
        Assert.All(provider.Models, model => Assert.True(
            model.Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase) || model.Id == "openrouter/free",
            $"{model.Id} is not a free model"));
        Assert.Contains(provider.Models, model => model.Id == provider.DefaultModel);
        Assert.Contains(provider.Models, model => model.Name.Contains("vision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Chat_posts_openai_shape_with_bearer_key_and_title_header()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { role = "assistant", content = "Hello from OpenRouter" } } }
        }));

        var reply = await new OpenRouterService(new HttpClient(handler))
            .ChatAsync(Config, "TABLE dbo.Customers", [new ChatMessage { Role = "user", Content = "hi" }]);

        Assert.Equal("Hello from OpenRouter", reply);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.RequestUri);
        Assert.Equal("Bearer sk-or-test", handler.Authorization);
        Assert.Equal("SQLAZOR", handler.TitleHeader);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(Config.Model, body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());

        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("TABLE dbo.Customers", messages[0].GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.String, messages[1].GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task Images_become_content_parts_with_a_data_url()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "I see a diagram" } } }
        }));

        await new OpenRouterService(new HttpClient(handler)).ChatAsync(Config, "schema",
        [
            new ChatMessage
            {
                Role = "user",
                Content = "describe this",
                Attachments =
                [
                    new ChatAttachment
                    {
                        FileName = "diagram.png",
                        MimeType = "image/png",
                        Content = Convert.ToBase64String([1, 2, 3]),
                        SizeBytes = 3
                    }
                ]
            }
        ]);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var content = body.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);

        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("describe this", content[0].GetProperty("text").GetString());

        Assert.Equal("image_url", content[1].GetProperty("type").GetString());
        Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String([1, 2, 3])}",
            content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Stream_skips_openrouter_comments_and_stops_at_done()
    {
        var sse = string.Join("\n\n",
            ": OPENROUTER PROCESSING",
            Delta("{\"type\":\"chart\","),
            Delta("\"labels\":[\"A\"]}"),
            "data: [DONE]",
            Delta("ignored after done")) + "\n\n";

        var handler = new RecordingHandler(_ => SseResponse(sse));
        var chunks = new List<string>();

        await foreach (var chunk in new OpenRouterService(new HttpClient(handler))
            .ChatStreamAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { "{\"type\":\"chart\",", "\"labels\":[\"A\"]}" }, chunks);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.RequestUri);
    }

    [Fact]
    public async Task Chat_falls_back_to_reasoning_when_content_is_null()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = (string?)null, reasoning = "OK" } }
            }
        }));

        var reply = await new OpenRouterService(new HttpClient(handler))
            .ChatAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]);

        Assert.Equal("OK", reply);
    }

    [Fact]
    public async Task Empty_completion_is_treated_as_a_successful_reply()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = (string?)null, reasoning = (string?)null } }
            }
        }));

        var reply = await new OpenRouterService(new HttpClient(handler))
            .ChatAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]);

        Assert.Equal(string.Empty, reply);
    }

    [Fact]
    public async Task Invalid_key_error_body_is_surfaced()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            new { error = new { message = "No auth credentials found", code = 401 } },
            HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OpenRouterService(new HttpClient(handler))
                .ChatAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]));

        Assert.Contains("401", ex.Message);
        Assert.Contains("No auth credentials found", ex.Message);
    }

    [Fact]
    public async Task Missing_api_key_fails_before_any_request()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new { choices = Array.Empty<object>() }));
        var config = new AiProviderConfig
        {
            Provider = AiProviderCatalog.OpenRouter,
            Endpoint = Config.Endpoint,
            Model = Config.Model
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OpenRouterService(new HttpClient(handler))
                .ChatAsync(config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]));

        Assert.Contains("API key", ex.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Json_generation_is_prompt_driven_and_strips_fences()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "```json\n{\"table\":\"Orders\"}\n```" } } }
        }));

        var json = await new OpenRouterService(new HttpClient(handler))
            .GenerateJsonAsync(Config, "Name this table");

        Assert.Equal("{\"table\":\"Orders\"}", json);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task Endpoint_may_already_include_the_chat_completions_path()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "ok" } } }
        }));

        await new OpenRouterService(new HttpClient(handler)).ChatAsync(
            new AiProviderConfig
            {
                Provider = AiProviderCatalog.OpenRouter,
                Endpoint = "https://openrouter.ai/api/v1/chat/completions/",
                Model = Config.Model,
                ApiKey = "k"
            },
            "schema",
            [new ChatMessage { Role = "user", Content = "hi" }]);

        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.RequestUri);
    }

    private static string Delta(string content) =>
        "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content } } } });

    private static HttpResponseMessage JsonResponse(object payload, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage SseResponse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public string? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string? TitleHeader { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            TitleHeader = request.Headers.TryGetValues("X-Title", out var title) ? title.FirstOrDefault() : null;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responder(request);
        }
    }
}
