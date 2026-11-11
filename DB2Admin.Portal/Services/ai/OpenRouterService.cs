using SQLAZOR.Models;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace SQLAZOR.Services;

/// <summary>
/// OpenRouter proxies many vendors behind the OpenAI chat-completions shape. Free models carry a
/// <c>:free</c> suffix and share a rate limit, so the catalog lists only curated free ids.
/// JSON generation is prompt-driven because not every proxied model honours <c>response_format</c>.
/// </summary>
public sealed class OpenRouterService : IChatProvider
{
    public const string DefaultEndpoint = "https://openrouter.ai/api/v1";

    private const string JsonSystemPrompt =
        "You return exactly one valid JSON object and nothing else - no prose, no Markdown fences.";

    private readonly HttpClient _http;

    public OpenRouterService(HttpClient http)
    {
        _http = http;
    }

    public string Id => AiProviderCatalog.OpenRouter;

    private static List<object> BuildMessages(string schemaContext, IReadOnlyList<ChatMessage> history)
    {
        var messages = new List<object>
        {
            new { role = "system", content = ChatSystemPrompt.Build(schemaContext) }
        };
        messages.AddRange(history.Select(m => (object)BuildMessage(m)));
        return messages;
    }

    private static Dictionary<string, object> BuildMessage(ChatMessage message)
    {
        var entry = new Dictionary<string, object>
        {
            ["role"] = message.Role
        };

        var images = message.Attachments
            .Where(a => a.IsImage && !string.IsNullOrWhiteSpace(a.Content))
            .ToList();

        if (images.Count == 0)
        {
            entry["content"] = message.Content;
            return entry;
        }

        // Vision models on OpenRouter receive OpenAI-style content parts with a data URL.
        var parts = new List<object>
        {
            new Dictionary<string, object>
            {
                ["type"] = "text",
                ["text"] = message.Content
            }
        };

        parts.AddRange(images.Select(image => (object)new Dictionary<string, object>
        {
            ["type"] = "image_url",
            ["image_url"] = new Dictionary<string, object>
            {
                ["url"] = ToDataUrl(image)
            }
        }));

        entry["content"] = parts;
        return entry;
    }

    private static string ToDataUrl(ChatAttachment attachment) =>
        $"data:{attachment.MimeType ?? "image/png"};base64,{attachment.Content}";

    public async Task<string> ChatAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        EnsureApiKey(config);

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
                $"Could not reach OpenRouter at {ResolveEndpoint(config.Endpoint)}. Check your connection. ({ex.Message})", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenRouter returned {(int)response.StatusCode}: {ExtractError(body) ?? body}");
        }

        var content = ReadMessageContent(body);
        if (content is not null)
            return content;

        var fallbackError = ExtractError(body);
        if (fallbackError is not null)
            throw new InvalidOperationException($"OpenRouter error: {fallbackError}");

        // A well-formed completion with no text is still a valid reply; reasoning models
        // occasionally answer a trivial connectivity prompt with an empty message.
        if (HasCompletion(body))
            return string.Empty;

        throw new InvalidOperationException("OpenRouter returned an unexpected response shape.");
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureApiKey(config);

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
                $"Could not reach OpenRouter at {ResolveEndpoint(config.Endpoint)}. Check your connection. ({ex.Message})", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"OpenRouter returned {(int)response.StatusCode}: {ExtractError(errorBody) ?? errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var sawAnyContent = false;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            // OpenRouter may interleave comments such as ": OPENROUTER PROCESSING" with the data events.
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
                continue;
            }

            using (parsed)
            {
                var streamError = ExtractError(parsed.RootElement);
                if (streamError is not null)
                    throw new InvalidOperationException($"OpenRouter error: {streamError}");

                var chunk = ReadDelta(parsed.RootElement);
                if (!string.IsNullOrEmpty(chunk))
                {
                    sawAnyContent = true;
                    yield return chunk;
                }
            }
        }

        if (!sawAnyContent)
            throw new InvalidOperationException("OpenRouter returned an empty streamed response.");
    }

    public async Task<string> GenerateJsonAsync(AiProviderConfig config, string prompt, CancellationToken ct = default)
    {
        EnsureApiKey(config);

        var payload = new
        {
            model = config.Model,
            messages = new object[]
            {
                new { role = "system", content = JsonSystemPrompt },
                new { role = "user", content = prompt }
            },
            temperature = 0.2,
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
                $"Could not reach OpenRouter at {ResolveEndpoint(config.Endpoint)}. Check your connection. ({ex.Message})", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenRouter returned {(int)response.StatusCode}: {ExtractError(body) ?? body}");
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

    private static void EnsureApiKey(AiProviderConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException(
                "OpenRouter needs an API key, even for free models. Paste one into the AI settings above, then try again.");
    }

    private static HttpRequestMessage CreateRequest(AiProviderConfig config, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl(config.Endpoint))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        request.Headers.TryAddWithoutValidation("X-Title", "SQLAZOR");
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

    private static bool HasCompletion(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("choices", out var choices) &&
                   choices.ValueKind == JsonValueKind.Array &&
                   choices.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadMessageContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
            return null;

        if (!choices[0].TryGetProperty("message", out var message))
            return null;

        if (message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(content.GetString()))
            return content.GetString();

        // Reasoning models served through OpenRouter can put the only text in "reasoning"
        // and leave "content" null, which otherwise looks like an unparseable response.
        return message.TryGetProperty("reasoning", out var reasoning) &&
               reasoning.ValueKind == JsonValueKind.String
            ? reasoning.GetString()
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
