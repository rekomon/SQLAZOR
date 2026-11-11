using SQLAZOR.Models;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace SQLAZOR.Services;

public sealed class OllamaService : IChatProvider
{
    private readonly HttpClient _http;

    public OllamaService(HttpClient http)
    {
        _http = http;
    }

    public string Id => AiProviderCatalog.Ollama;

    private static List<object> BuildChatMessages(string schemaContext, IReadOnlyList<ChatMessage> history)
    {
        var messages = new List<object>
        {
            new { role = "system", content = ChatSystemPrompt.Build(schemaContext) }
        };
        messages.AddRange(history.Select(m => (object)BuildChatMessage(m)));
        return messages;
    }

    private static Dictionary<string, object> BuildChatMessage(ChatMessage message)
    {
        var entry = new Dictionary<string, object>
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };

        // Ollama takes raw base64 (no data: prefix) in an "images" array per message.
        var images = message.Attachments
            .Where(a => a.IsImage && !string.IsNullOrWhiteSpace(a.Content))
            .Select(a => a.Content)
            .ToList();

        if (images.Count > 0)
            entry["images"] = images;

        return entry;
    }

    #region Chat Async
    public async Task<string> ChatAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        var endpoint = config.Endpoint.Trim();
        var url = BuildUrl(endpoint, "/api/chat");
        var payload = new { model = config.Model, messages = BuildChatMessages(schemaContext, history), stream = false };
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not reach Ollama at {endpoint}. Is it running? ({ex.Message})", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = TryExtractError(body) ?? body;
            throw new InvalidOperationException($"Ollama returned {(int)response.StatusCode}: {errorText}");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("message", out var messageEl) &&
            messageEl.TryGetProperty("content", out var contentEl))
        {
            return contentEl.GetString() ?? string.Empty;
        }

        var fallbackError = TryExtractError(body);
        if (fallbackError is not null)
            throw new InvalidOperationException($"Ollama error: {fallbackError}");

        throw new InvalidOperationException("Ollama returned an unexpected response shape.");
    }

    #endregion


    #region Chat Stream Async

    public async IAsyncEnumerable<string> ChatStreamAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var endpoint = config.Endpoint.Trim();
        var url = BuildUrl(endpoint, "/api/chat");
        var payload = new { model = config.Model, messages = BuildChatMessages(schemaContext, history), stream = true };
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response;
        try
        {
            // Read headers as soon as they arrive rather than buffering the whole (chunked, unbounded)
            // response body - that's what actually lets us start yielding text before Ollama is done.
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not reach Ollama at {endpoint}. Is it running? ({ex.Message})", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var errorText = TryExtractError(errorBody) ?? errorBody;
            throw new InvalidOperationException($"Ollama returned {(int)response.StatusCode}: {errorText}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var sawAnyContent = false;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Ollama streams one NDJSON object per line - {"message":{"content":"chunk"},"done":false}
            // repeated, then a final {"done":true,...} with aggregate stats. A malformed line (rare,
            // e.g. a truncated chunk from a dropped connection) is skipped rather than aborting the
            // whole reply - better a slightly short answer than losing everything already streamed.
            string? chunk = null;
            var isDone = false;
            string? streamError = null;

            try
            {
                using var lineDoc = JsonDocument.Parse(line);
                if (lineDoc.RootElement.TryGetProperty("error", out var errorEl))
                {
                    streamError = errorEl.GetString();
                }
                else
                {
                    if (lineDoc.RootElement.TryGetProperty("message", out var messageEl) &&
                        messageEl.TryGetProperty("content", out var contentEl))
                    {
                        chunk = contentEl.GetString();
                    }

                    if (lineDoc.RootElement.TryGetProperty("done", out var doneEl) && doneEl.ValueKind == JsonValueKind.True)
                    {
                        isDone = true;
                    }
                }
            }
            catch (JsonException)
            {
                continue;
            }

            if (streamError is not null)
            {
                throw new InvalidOperationException($"Ollama error: {streamError}");
            }

            if (!string.IsNullOrEmpty(chunk))
            {
                sawAnyContent = true;
                yield return chunk;
            }

            if (isDone)
            {
                yield break;
            }
        }

        if (!sawAnyContent)
        {
            throw new InvalidOperationException("Ollama returned an empty streamed response.");
        }
    }

    #endregion
    public async Task<string> GenerateJsonAsync(AiProviderConfig config, string prompt,
        CancellationToken ct = default)
    {
        var url = BuildUrl(config.Endpoint.Trim(), "/api/generate");

        var payload = new
        {
            model = config.Model,
            prompt,
            format = "json",
            stream = false,
            options = new { temperature = 0.2 }
        };
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not reach Ollama at {config.Endpoint}. Is it running? ({ex.Message})", ex);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = TryExtractError(body) ?? body;
            throw new InvalidOperationException($"Ollama returned {(int)response.StatusCode}: {errorText}");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("response", out var responseEl))
        {
            return responseEl.GetString() ?? "{}";
        }

        var fallbackError = TryExtractError(body);
        if (fallbackError is not null)
            throw new InvalidOperationException($"Ollama error: {fallbackError}");

        throw new InvalidOperationException("Ollama returned an unexpected response shape.");
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
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string BuildUrl(string endpoint, string path)
    {
        var trimmed = endpoint.TrimEnd('/');
        return trimmed + path;
    }

    private static string? TryExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errorEl))
            {
                return errorEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through and let the caller use the raw body.
        }

        return null;
    }
}
