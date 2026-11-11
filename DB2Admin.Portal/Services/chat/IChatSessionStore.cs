using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>Persists AI-assistant conversations between browser sessions.</summary>
public interface IChatSessionStore
{
    /// <summary>
    /// Lists saved conversations, pinned first and newest first within each group. When
    /// <paramref name="search"/> is set, matches title, database name, or message text.
    /// </summary>
    Task<IReadOnlyList<ChatSessionSummary>> ListAsync(string? search = null, CancellationToken ct = default);
    Task<ChatSession?> LoadAsync(string id, CancellationToken ct = default);
    Task SaveAsync(ChatSession session, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
