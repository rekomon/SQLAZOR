using SQLAZOR.Models;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace SQLAZOR.Services;

/// <summary>
/// DeepSeek uses the OpenAI chat-completions shape: a bearer key, <c>choices[].message.content</c>
/// for one-shot replies, and server-sent <c>data:</c> events carrying <c>choices[].delta.content</c>
/// when streaming. JSON extraction uses <c>response_format</c> where the model supports it.
/// </summary>
public sealed class DeepSeekService : IChatProvider
{
    public const string DefaultEndpoint = "https://api.deepseek.com";

    private const string JsonSystemPrompt =
        "You return exactly one valid JSON object and nothing else - no prose, no Markdown fences.";

    private readonly HttpClient _http;

    public DeepSeekService(HttpClient http)
    {
        _http = http;
    }

    public string Id => AiProviderCatalog.DeepSeek;

    private static List<object> BuildMessages(string schemaContext, IReadOnlyList<ChatMessage> history)
    {
        var messages = new List<object>
        {
            new { role = "system", content = ChatSystemPrompt.Build(schemaContext) }
        };
        messages.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Content }));
        return messages;
    }

    public async Task<string> ChatAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        EnsureApiKey(config);
        EnsureNoImages(history);

        var payload = new
        {
            model = config.Model,
            messages = BuildMessages(schemaContext, history),
            temperature = 0.3,
            stream = false
        };

        using var request = CreateRequest(config, payload);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not reach DeepSeek at {ResolveEndpoint(config.Endpoint)}. Check your connection. ({ex.Message})", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"DeepSeek returned {(int)response.StatusCode}: {ExtractError(body) ?? body}");
        }

        var content = ReadMessageContent(body);
        if (content is not null)
            return content;

        var fallbackError = ExtractError(body);
        if (fallbackError is not null)
            throw new InvalidOperationException($"DeepSeek error: {fallbackError}");

        throw new InvalidOperationException("DeepSeek returned an unexpected response shape.");
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureApiKey(config);
        EnsureNoImages(history);

        var payload = new
        {
            model = config.Model,
            messages = BuildMessages(schemaContext, history),
            temperature = 0.3,
            stream = true
        };

        using var request = CreateRequest(config, payload);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not reach DeepSeek at {ResolveEndpoint(config.Endpoint)}. Check your connection. ({ex.Message})", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"DeepSeek returned {(int)response.StatusCode}: {ExtractError(errorBody) ?? errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var sawAnyContent = false;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line[5..].Trim();
            if (data.Length == 0)
                continue;
            if (data == "[DONE]")
                break;

            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(data);
            }
            catch (JsonException)
            {
                // A truncated chunk from a dropped connection should not throw away text already streamed.
                continue;
            }

            using (parsed)
            {
                var streamError = ExtractError(parsed.RootElement);
                if (streamError is not null)
                    throw new InvalidOperationException($"DeepSeek error: {streamError}");

                var chunk = ReadDelta(parsed.RootElement);
                if (!string.IsNullOrEmpty(chunk))
                {
                    sawAnyContent = true;
                    yield return chunk;
                }
            }
        }

        if (!sawAnyContent)
            throw new InvalidOperationException("DeepSeek returned an empty streamed response.");
    }

    public async Task<string> GenerateJsonAsync(AiProviderConfig config, string prompt, CancellationToken ct = default)
    {
        EnsureApiKey(config);

        var messages = new object[]
        {
            new { role = "system", content = JsonSystemPrompt },
            new { role = "user", content = prompt }
        };

        object payload = UsesJsonMode(config.Model)
            ? new { model = config.Model, messages, temperature = 0.2, stream = false, response_format = new { type = "json_object" } }
            : new { model = config.Model, messages, temperature = 0.2, stream = false };

        using var request = CreateRequest(config, payload);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not reach DeepSeek at {ResolveEndpoint(config.Endpoint)}. Check your connection. ({ex.Message})", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"DeepSeek returned {(int)response.StatusCode}: {ExtractError(body) ?? body}");
        }

        var content = ReadMessageContent(body) ?? "{}";
        return CleanJson(content);
    }

    public async Task<(bool Success, string? Error)> TestConnectionAsync(AiProviderConfig config, CancellationToken ct = default)
    {
        try
        {
            var reply = await ChatAsync(
                config,
                schemaContext: "(none — this is just a connectivity test)",
                history: [new ChatMessage { Role = "user", Content = "Reply with just the word OK." }],
                ct);

            return (true, reply.Length > 0 ? null : "Model replied with empty content.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }
    }

    // DeepSeek enables JSON mode for deepseek-chat; the reasoner model rejects response_format,
    // so it is prompt-driven instead and any fence it adds is stripped before parsing.
    private static bool UsesJsonMode(string? model) =>
        !string.Equals(model, "deepseek-reasoner", StringComparison.OrdinalIgnoreCase);

    private static void EnsureApiKey(AiProviderConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException(
                "DeepSeek needs an API key. Paste one into the AI settings above, then try again.");
    }

    // The DeepSeek chat-completions API is text-only; failing here is clearer than letting
    // the request silently drop the images.
    private static void EnsureNoImages(IReadOnlyList<ChatMessage> history)
    {
        if (history.Any(m => m.Attachments.Any(a => a.IsImage)))
            throw new InvalidOperationException(
                "DeepSeek does not accept images. Remove the image attachments or switch to Ollama with a vision model (e.g. LLaVA).");
    }

    private static HttpRequestMessage CreateRequest(AiProviderConfig config, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl(config.Endpoint))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        return request;
    }

    private static string ResolveEndpoint(string? endpoint) =>
        string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();

    private static string ChatUrl(string? endpoint)
    {
        var trimmed = ResolveEndpoint(endpoint).TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/chat/completions";
    }

    private static string? ReadMessageContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return ReadMessageContent(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadMessageContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
            return null;

        return choices[0].TryGetProperty("message", out var message) &&
               message.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
    }

    private static string? ReadDelta(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
            return null;

        return choices[0].TryGetProperty("delta", out var delta) &&
               delta.TryGetProperty("content", out var content) &&
               content.ValueKind == JsonValueKind.String
            ? content.GetString()
            : null;
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return ExtractError(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error))
            return null;

        return error.ValueKind switch
        {
            JsonValueKind.String => error.GetString(),
            JsonValueKind.Object when error.TryGetProperty("message", out var message) => message.GetString(),
            _ => "unknown error"
        };
    }

    private static string CleanJson(string content)
    {
        var text = content.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            if (firstNewLine >= 0)
                text = text[(firstNewLine + 1)..];
            if (text.EndsWith("```", StringComparison.Ordinal))
                text = text[..^3];
            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
