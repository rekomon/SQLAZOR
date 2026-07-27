using SQLAZOR.Models;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SQLAZOR.Services;

public sealed class OllamaService : IOllamaService
{
    private readonly HttpClient _http;

    private const string SystemPromptTemplate =
        "You are a helpful assistant embedded in a SQL Server code-generation tool called SQLAZOR. " +
        "You answer questions about the database schema below, explain relationships, and can write T-SQL " +
        "or C# / EF Core LINQ queries when asked. DbSet property names are the pluralized entity class names. " +
        "IMPORTANT: whenever you write a T-SQL query, put it in a code fence tagged 'sql' (```sql ... ```) - " +
        "the user has a 'Run query' button that appears on sql-tagged blocks and executes them directly against " +
        "their database (read-only SELECT/WITH statements only), so getting the tag right matters. " +
        "Reply in the same language the user writes in (Arabic or English). Be concise and use code blocks for code.\n\n" +
        "--- SCHEMA CONTEXT ---\n{0}\n--- END SCHEMA CONTEXT ---";

    public OllamaService(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> ChatAsync(
        string endpoint,
        string model,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        var url = BuildUrl(endpoint, "/api/chat");

        var messages = new List<object>
        {
            new { role = "system", content = string.Format(SystemPromptTemplate, schemaContext) }
        };
        messages.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Content }));

        var payload = new { model, messages, stream = false };
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

    public async IAsyncEnumerable<string> StreamChatAsync(string endpoint, string model,
        string prompt,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint, "/api/chat");

        
        var messages = new List<object>
        {
            new { role = "system", content = string.Format(SystemPromptTemplate, prompt) }
        };
        messages.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Content }));

        var payload = new { model, messages,
            format = "json",
            stream = true,
            options = new { temperature = 0.2 }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(
            url,
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);

        string line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null && !cancellationToken.IsCancellationRequested)
        {

            Console.WriteLine(line);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Some streaming endpoints prefix lines with "data: "
            var trimmed = line.Trim();
            if (trimmed.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(6).Trim();

            if (string.Equals(trimmed, "[DONE]", StringComparison.OrdinalIgnoreCase))
                yield break;

            
                using var jsonDoc = JsonDocument.Parse(trimmed);
                var root = jsonDoc.RootElement;

                // Look for common response properties
                if (root.TryGetProperty("response", out var responseProp) &&
                    responseProp.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(responseProp.GetString()))
                {
                Console.WriteLine(responseProp.GetString()!);
                yield return responseProp.GetString()!;
                    continue;
                }

                if (root.TryGetProperty("content", out var contentProp) &&
                    contentProp.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(contentProp.GetString()))
                {
                Console.WriteLine(contentProp.GetString()!);
                yield return contentProp.GetString()!;
                    continue;
                }

                // Fallback: if root itself is a string
                if (root.ValueKind == JsonValueKind.String)
                {
                    var s = root.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    Console.WriteLine(s);
                    yield return s;
                }
                }
            
        }
    }


    public async Task<string> GenerateJsonAsync(string endpoint, string model, string prompt,
        CancellationToken ct = default)
    {
        var url = BuildUrl(endpoint, "/api/generate");

        var payload = new
        {
            model,
            prompt,
            format = "json",
            stream = true,
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
                $"Could not reach Ollama at {endpoint}. Is it running? ({ex.Message})", ex);
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

    public async Task<(bool Success, string? Error)> TestConnectionAsync(string endpoint, string model, CancellationToken ct = default)
    {
        try
        {
            var reply = await ChatAsync(
                endpoint,
                model,
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
