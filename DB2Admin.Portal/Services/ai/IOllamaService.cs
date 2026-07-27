using SQLAZOR.Models;
namespace SQLAZOR.Services;

public interface IOllamaService
{
    /// <summary>
    /// Sends the conversation (plus a schema-context system prompt) to a local/remote Ollama
    /// instance and returns the assistant's reply text. Throws on connection or API errors —
    /// callers should catch and surface a friendly message.
    /// </summary>
    Task<string> ChatAsync(
        string endpoint,
        string model,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamChatAsync(
         string endpoint,
        string model,
        string prompt,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default);

    /// <summary>Quick reachability + model-availability check, used for a "Test connection" button.</summary>
    Task<(bool Success, string? Error)> TestConnectionAsync(string endpoint, string model, CancellationToken ct = default);

    /// <summary>
    /// Single-shot, non-chat completion with Ollama's JSON output mode forced on
    /// (<c>"format": "json"</c>). Used for structured extraction tasks (e.g. naming suggestions)
    /// where free-form chat text would need fragile parsing. Returns the raw JSON string —
    /// callers deserialize it themselves and should tolerate malformed output.
    /// </summary>
    Task<string> GenerateJsonAsync(string endpoint, string model, string prompt, CancellationToken ct = default);
}
