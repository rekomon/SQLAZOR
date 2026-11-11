using System.Text.Json;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Stores one JSON file per conversation under %LOCALAPPDATA%\SQLAZOR\chat-sessions.
/// Writes go through a temp file + move so a crash mid-write cannot truncate an existing
/// conversation, and malformed files are skipped instead of breaking the whole list.
/// </summary>
public sealed class FileChatSessionStore : IChatSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public FileChatSessionStore() : this(DefaultDirectory())
    {
    }

    public FileChatSessionStore(string directory)
    {
        _directory = directory;
    }

    public static string DefaultDirectory() =>
        Path.Combine(AppDataPath.BaseDirectory(), "SQLAZOR", "chat-sessions");

    public async Task<IReadOnlyList<ChatSessionSummary>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        var term = search?.Trim();

        await _gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(_directory))
                return [];

            var summaries = new List<ChatSessionSummary>();
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                ct.ThrowIfCancellationRequested();

                var session = await ReadAsync(file, ct);
                if (session is null)
                    continue;

                if (!string.IsNullOrEmpty(term) && !Matches(session, term))
                    continue;

                summaries.Add(new ChatSessionSummary
                {
                    Id = session.Id,
                    Title = session.Title,
                    DatabaseKey = session.DatabaseKey,
                    DatabaseName = session.DatabaseName,
                    MessageCount = session.Messages.Count,
                    Pinned = session.Pinned,
                    UpdatedAt = session.UpdatedAt
                });
            }

            return summaries
                .OrderByDescending(s => s.Pinned)
                .ThenByDescending(s => s.UpdatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool Matches(ChatSession session, string term)
    {
        if (session.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            session.DatabaseName.Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;

        return session.Messages.Any(m =>
            m.Content?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
    }

    public async Task<ChatSession?> LoadAsync(string id, CancellationToken ct = default)
    {
        if (!IsValidId(id))
            return null;

        await _gate.WaitAsync(ct);
        try
        {
            return await ReadAsync(PathFor(id), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ChatSession session, CancellationToken ct = default)
    {
        if (!IsValidId(session.Id))
            session.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(session.Title))
            session.Title = "New conversation";

        session.UpdatedAt = DateTimeOffset.Now;

        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_directory);

            var path = PathFor(session.Id);
            var temp = path + ".tmp";
            var json = JsonSerializer.Serialize(session, JsonOptions);

            await File.WriteAllTextAsync(temp, json, ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!IsValidId(id))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            var path = PathFor(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ChatSession?> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var session = JsonSerializer.Deserialize<ChatSession>(json, JsonOptions);
            if (session is null)
                return null;

            session.Messages ??= [];
            session.Id = Path.GetFileNameWithoutExtension(path);
            return session;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string PathFor(string id) => Path.Combine(_directory, id + ".json");

    // Session ids come from the browser, so only accept the 32-hex "N" GUID shape we generate -
    // that rules out ".." and absolute paths before they ever reach Path.Combine.
    private static bool IsValidId(string? id) =>
        id is { Length: 32 } && id.All(Uri.IsHexDigit);
}
