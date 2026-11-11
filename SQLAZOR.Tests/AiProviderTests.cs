using SQLAZOR.Models;
using SQLAZOR.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class DeepSeekServiceTests
{
    private static readonly AiProviderConfig Config = new()
    {
        Provider = AiProviderCatalog.DeepSeek,
        Endpoint = "https://api.deepseek.com",
        Model = "deepseek-chat",
        ApiKey = "test-key"
    };

    [Fact]
    public async Task Chat_posts_openai_shape_with_bearer_key()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { role = "assistant", content = "Hello from DeepSeek" } } }
        }));

        var reply = await new DeepSeekService(new HttpClient(handler))
            .ChatAsync(Config, "TABLE dbo.Customers", [new ChatMessage { Role = "user", Content = "hi" }]);

        Assert.Equal("Hello from DeepSeek", reply);
        Assert.Equal("https://api.deepseek.com/chat/completions", handler.RequestUri);
        Assert.Equal("Bearer test-key", handler.Authorization);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("deepseek-chat", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());

        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("TABLE dbo.Customers", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Error_body_object_is_surfaced()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            new { error = new { message = "Insufficient Balance", type = "insufficient_balance" } },
            HttpStatusCode.PaymentRequired));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeepSeekService(new HttpClient(handler))
                .ChatAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]));

        Assert.Contains("402", ex.Message);
        Assert.Contains("Insufficient Balance", ex.Message);
    }

    [Fact]
    public async Task Missing_api_key_fails_before_any_request()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new { choices = Array.Empty<object>() }));
        var config = new AiProviderConfig { Provider = AiProviderCatalog.DeepSeek, Endpoint = Config.Endpoint, Model = Config.Model };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeepSeekService(new HttpClient(handler))
                .ChatAsync(config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]));

        Assert.Contains("API key", ex.Message);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Endpoint_may_already_include_the_v1_segment()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "ok" } } }
        }));

        await new DeepSeekService(new HttpClient(handler)).ChatAsync(
            new AiProviderConfig { Provider = AiProviderCatalog.DeepSeek, Endpoint = "https://api.deepseek.com/v1/", Model = "deepseek-chat", ApiKey = "k" },
            "schema",
            [new ChatMessage { Role = "user", Content = "hi" }]);

        Assert.Equal("https://api.deepseek.com/v1/chat/completions", handler.RequestUri);
    }

    [Fact]
    public async Task Stream_yields_deltas_in_order_and_stops_at_done()
    {
        var sse = string.Join("\n\n",
            ": keep-alive",
            "event: message",
            Delta("{\"type\":\"chart\","),
            Delta("\"labels\":[\"A\"]}"),
            "data: [DONE]",
            Delta("ignored after done")) + "\n\n";

        var handler = new RecordingHandler(_ => SseResponse(sse));
        var chunks = new List<string>();

        await foreach (var chunk in new DeepSeekService(new HttpClient(handler))
            .ChatStreamAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { "{\"type\":\"chart\",", "\"labels\":[\"A\"]}" }, chunks);
        Assert.Equal("https://api.deepseek.com/chat/completions", handler.RequestUri);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task Stream_skips_malformed_chunks_but_keeps_the_rest()
    {
        var sse = "data: {not json}\n\n" + Delta("partial") + "\n\ndata: [DONE]\n\n";
        var handler = new RecordingHandler(_ => SseResponse(sse));
        var chunks = new List<string>();

        await foreach (var chunk in new DeepSeekService(new HttpClient(handler))
            .ChatStreamAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { "partial" }, chunks);
    }

    [Fact]
    public async Task Stream_surfaces_error_events()
    {
        var handler = new RecordingHandler(_ => SseResponse(
            "data: {\"error\":{\"message\":\"rate limited\"}}\n\n"));
        var service = new DeepSeekService(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.ChatStreamAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]))
            {
            }
        });

        Assert.Contains("rate limited", ex.Message);
    }

    [Fact]
    public async Task Stream_without_content_throws()
    {
        var handler = new RecordingHandler(_ => SseResponse("data: [DONE]\n\n"));
        var service = new DeepSeekService(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.ChatStreamAsync(Config, "schema", [new ChatMessage { Role = "user", Content = "hi" }]))
            {
            }
        });

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task Json_generation_requests_json_mode_for_chat_model()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "{\"table\":\"Orders\"}" } } }
        }));

        var json = await new DeepSeekService(new HttpClient(handler))
            .GenerateJsonAsync(Config, "Name this table");

        Assert.Equal("{\"table\":\"Orders\"}", json);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Json_generation_for_reasoner_skips_json_mode_and_strips_fences()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "```json\n{\"table\":\"Orders\"}\n```" } } }
        }));

        var json = await new DeepSeekService(new HttpClient(handler)).GenerateJsonAsync(
            new AiProviderConfig { Provider = AiProviderCatalog.DeepSeek, Endpoint = Config.Endpoint, Model = "deepseek-reasoner", ApiKey = "k" },
            "Name this table");

        Assert.Equal("{\"table\":\"Orders\"}", json);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task Test_connection_reports_success_and_failure()
    {
        var ok = new DeepSeekService(new HttpClient(new RecordingHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "OK" } } }
        }))));

        Assert.True((await ok.TestConnectionAsync(Config)).Success);

        var broken = new DeepSeekService(new HttpClient(new RecordingHandler(_ =>
            throw new HttpRequestException("no route to host"))));

        var (success, error) = await broken.TestConnectionAsync(Config);
        Assert.False(success);
        Assert.Contains("Could not reach DeepSeek", error);
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
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responder(request);
        }
    }
}

public sealed class ChatProviderServiceTests
{
    [Fact]
    public async Task Dispatch_uses_the_provider_named_in_the_config()
    {
        var deepSeek = new RecordingResponder("from deepseek");
        var service = new ChatProviderService(
        [
            new OllamaService(new HttpClient(new RecordingResponder("from ollama"))),
            new DeepSeekService(new HttpClient(deepSeek))
        ]);

        var reply = await service.ChatAsync(
            new AiProviderConfig { Provider = AiProviderCatalog.DeepSeek, Endpoint = "https://api.deepseek.com", Model = "deepseek-chat", ApiKey = "k" },
            "schema",
            [new ChatMessage { Role = "user", Content = "hi" }]);

        Assert.Equal("from deepseek", reply);
        Assert.Equal(1, deepSeek.Calls);
    }

    [Fact]
    public async Task Unknown_provider_fails_loudly()
    {
        var service = new ChatProviderService([new OllamaService(new HttpClient(new RecordingResponder("x")))]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateJsonAsync(new AiProviderConfig { Provider = "gemini" }, "prompt"));
    }

    private sealed class RecordingResponder : HttpMessageHandler
    {
        private readonly string _content;

        public RecordingResponder(string content)
        {
            _content = content;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        choices = new[] { new { message = new { content = _content } } },
                        message = new { content = _content }
                    }),
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}

public sealed class AiSettingsAndStateTests
{
    [Fact]
    public async Task Store_round_trips_provider_and_key()
    {
        var path = Path.Combine(Path.GetTempPath(), "sqlazor-tests", Guid.NewGuid().ToString("N"), "ai-settings.json");
        try
        {
            var store = new FileAiSettingsStore(path);
            await store.SaveAsync(new AiSettingsSnapshot
            {
                Provider = AiProviderCatalog.DeepSeek,
                Configs =
                {
                    [AiProviderCatalog.DeepSeek] = new AiProviderConfig
                    {
                        Provider = AiProviderCatalog.DeepSeek,
                        Endpoint = "https://api.deepseek.com",
                        Model = "deepseek-reasoner",
                        ApiKey = "sk-local-only"
                    }
                }
            });

            var loaded = store.Load();

            Assert.Equal(AiProviderCatalog.DeepSeek, loaded.Provider);
            Assert.Equal("deepseek-reasoner", loaded.Configs[AiProviderCatalog.DeepSeek].Model);
            Assert.Equal("sk-local-only", loaded.Configs[AiProviderCatalog.DeepSeek].ApiKey);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_settings_file_falls_back_to_defaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sqlazor-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "ai-settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(path, "{not json");

            var snapshot = new FileAiSettingsStore(path).Load();

            Assert.Equal(AiProviderCatalog.Ollama, snapshot.Provider);
            Assert.Empty(snapshot.Configs);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task State_loads_the_saved_provider_and_fills_in_missing_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), "sqlazor-tests", Guid.NewGuid().ToString("N"), "ai-settings.json");
        try
        {
            var store = new FileAiSettingsStore(path);
            await store.SaveAsync(new AiSettingsSnapshot
            {
                Provider = AiProviderCatalog.DeepSeek,
                Configs =
                {
                    [AiProviderCatalog.DeepSeek] = new AiProviderConfig
                    {
                        Provider = AiProviderCatalog.DeepSeek,
                        Endpoint = "",
                        Model = "",
                        ApiKey = "k"
                    }
                }
            });

            var state = new GenerationState(store);

            Assert.Equal(AiProviderCatalog.DeepSeek, state.AiProvider);
            Assert.Equal("https://api.deepseek.com", state.CurrentAiConfig.Endpoint);
            Assert.Equal("deepseek-chat", state.CurrentAiConfig.Model);
            Assert.Equal("k", state.CurrentAiConfig.ApiKey);

            // Ollama is still available with catalog defaults for users who switch back.
            Assert.True(state.AiConfigs.ContainsKey(AiProviderCatalog.Ollama));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void State_without_a_store_uses_catalog_defaults()
    {
        var state = new GenerationState();

        Assert.Equal(AiProviderCatalog.Ollama, state.AiProvider);
        Assert.Equal(AiProviderCatalog.Get(AiProviderCatalog.Ollama).DefaultEndpoint, state.CurrentAiConfig.Endpoint);
        Assert.Equal(AiProviderCatalog.Get(AiProviderCatalog.Ollama).DefaultModel, state.CurrentAiConfig.Model);
        Assert.Empty(state.CurrentAiConfig.ApiKey);
    }
}
