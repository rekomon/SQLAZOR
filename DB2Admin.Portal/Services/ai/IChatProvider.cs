using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>One AI vendor adapter. Consumers resolve the right one through <see cref="IChatProviderService"/>.</summary>
public interface IChatProvider
{
    string Id { get; }

    Task<string> ChatAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    IAsyncEnumerable<string> ChatStreamAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    Task<(bool Success, string? Error)> TestConnectionAsync(AiProviderConfig config, CancellationToken ct = default);

    Task<string> GenerateJsonAsync(AiProviderConfig config, string prompt, CancellationToken ct = default);
}
