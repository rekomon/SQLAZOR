using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Stores conversations in a local SQLite database (chat.db under the SQLAZOR app-data
/// directory, resolved by AppDataPath so it survives system-profile identities). Metadata
/// columns drive listing and search; the full message list (including attachments) is kept
/// as JSON so exports keep their existing shape. Conversations previously saved as JSON
/// files by FileChatSessionStore are imported once on first use and moved to an
/// ".imported" backup folder beside the old directory.
/// </summary>
public sealed class SqliteChatSessionStore : IChatSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databasePath;
    private readonly string _legacyDirectory;
    private bool _initialized;

    public SqliteChatSessionStore() : this(DefaultDatabasePath(), FileChatSessionStore.DefaultDirectory())
    {
    }

    public SqliteChatSessionStore(string databasePath, string? legacyDirectory = null)
    {
        _databasePath = databasePath;
        _legacyDirectory = legacyDirectory ?? FileChatSessionStore.DefaultDirectory();
    }

    public static string DefaultDatabasePath() =>
        Path.Combine(AppDataPath.BaseDirectory(), "SQLAZOR", "chat.db");

    public async Task<IReadOnlyList<ChatSessionSummary>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        var term = search?.Trim();

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureInitializedAsync(ct);

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, database_key, database_name, message_count, pinned, updated_at
                FROM chat_sessions
                WHERE @term IS NULL
                   OR title LIKE @pattern ESCAPE '\'
                   OR database_name LIKE @pattern ESCAPE '\'
                   OR messages_json LIKE @pattern ESCAPE '\'
                ORDER BY pinned DESC, updated_ticks DESC;
                """;
            command.Parameters.AddWithValue("@term", string.IsNullOrEmpty(term) ? DBNull.Value : term);
            command.Parameters.AddWithValue("@pattern", string.IsNullOrEmpty(term) ? DBNull.Value : $"%{EscapeLike(term!)}%");

            var summaries = new List<ChatSessionSummary>();
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                summaries.Add(new ChatSessionSummary
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    DatabaseKey = reader.GetString(2),
                    DatabaseName = reader.GetString(3),
                    MessageCount = reader.GetInt32(4),
                    Pinned = reader.GetInt32(5) != 0,
                    UpdatedAt = ParseTimestamp(reader.GetString(6))
                });
            }

            return summaries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChatSession?> LoadAsync(string id, CancellationToken ct = default)
    {
        if (!IsValidId(id))
            return null;

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureInitializedAsync(ct);

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, database_key, database_name, provider, model, endpoint,
                       pinned, created_at, updated_at, messages_json
                FROM chat_sessions
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("@id", id);

            using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            var session = new ChatSession
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                DatabaseKey = reader.GetString(2),
                DatabaseName = reader.GetString(3),
                Provider = reader.GetString(4),
                Model = reader.GetString(5),
                Endpoint = reader.GetString(6),
                Pinned = reader.GetInt32(7) != 0,
                CreatedAt = ParseTimestamp(reader.GetString(8)),
                UpdatedAt = ParseTimestamp(reader.GetString(9)),
                Messages = JsonSerializer.Deserialize<List<ChatMessage>>(reader.GetString(10), JsonOptions) ?? []
            };
            return session;
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
        session.Messages ??= [];

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureInitializedAsync(ct);

            using var connection = Open();
            Upsert(connection, session);
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
            await EnsureInitializedAsync(ct);

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM chat_sessions WHERE id = @id;";
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_sessions (
                id            TEXT PRIMARY KEY,
                title         TEXT NOT NULL,
                database_key  TEXT NOT NULL DEFAULT '',
                database_name TEXT NOT NULL DEFAULT '',
                provider      TEXT NOT NULL DEFAULT '',
                model         TEXT NOT NULL DEFAULT '',
                endpoint      TEXT NOT NULL DEFAULT '',
                pinned        INTEGER NOT NULL DEFAULT 0,
                message_count INTEGER NOT NULL DEFAULT 0,
                created_at    TEXT NOT NULL,
                updated_at    TEXT NOT NULL,
                updated_ticks INTEGER NOT NULL,
                messages_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_chat_sessions_updated ON chat_sessions(updated_ticks DESC);
            PRAGMA journal_mode=WAL;
            """;
        await command.ExecuteNonQueryAsync(ct);

        await MigrateJsonFilesAsync(connection, ct);

        _initialized = true;
    }

    private static void Upsert(SqliteConnection connection, ChatSession session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_sessions (id, title, database_key, database_name, provider, model, endpoint,
                                       pinned, message_count, created_at, updated_at, updated_ticks, messages_json)
            VALUES (@id, @title, @databaseKey, @databaseName, @provider, @model, @endpoint,
                    @pinned, @messageCount, @createdAt, @updatedAt, @updatedTicks, @messagesJson)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                database_key = excluded.database_key,
                database_name = excluded.database_name,
                provider = excluded.provider,
                model = excluded.model,
                endpoint = excluded.endpoint,
                pinned = excluded.pinned,
                message_count = excluded.message_count,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                updated_ticks = excluded.updated_ticks,
                messages_json = excluded.messages_json;
            """;
        command.Parameters.AddWithValue("@id", session.Id);
        command.Parameters.AddWithValue("@title", session.Title);
        command.Parameters.AddWithValue("@databaseKey", session.DatabaseKey);
        command.Parameters.AddWithValue("@databaseName", session.DatabaseName);
        command.Parameters.AddWithValue("@provider", session.Provider);
        command.Parameters.AddWithValue("@model", session.Model);
        command.Parameters.AddWithValue("@endpoint", session.Endpoint);
        command.Parameters.AddWithValue("@pinned", session.Pinned ? 1 : 0);
        command.Parameters.AddWithValue("@messageCount", session.Messages.Count);
        command.Parameters.AddWithValue("@createdAt", session.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@updatedAt", session.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@updatedTicks", session.UpdatedAt.UtcTicks);
        command.Parameters.AddWithValue("@messagesJson", JsonSerializer.Serialize(session.Messages, JsonOptions));
        command.ExecuteNonQuery();
    }

    private async Task MigrateJsonFilesAsync(SqliteConnection connection, CancellationToken ct)
    {
        if (!Directory.Exists(_legacyDirectory))
            return;

        var imported = 0;
        foreach (var file in Directory.EnumerateFiles(_legacyDirectory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var session = JsonSerializer.Deserialize<ChatSession>(json, JsonOptions);
                if (session is null || !IsValidId(session.Id))
                    continue;

                using var exists = connection.CreateCommand();
                exists.CommandText = "SELECT COUNT(*) FROM chat_sessions WHERE id = @id;";
                exists.Parameters.AddWithValue("@id", session.Id);
                if ((long)(await exists.ExecuteScalarAsync(ct))! > 0)
                {
                    MoveToBackup(file);
                    continue;
                }

                session.Messages ??= [];
                Upsert(connection, session);
                imported++;
                MoveToBackup(file);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Leave the file in place so a later startup can retry the import.
            }
        }

        void MoveToBackup(string file)
        {
            var backup = _legacyDirectory + ".imported";
            Directory.CreateDirectory(backup);
            File.Move(file, Path.Combine(backup, Path.GetFileName(file)), overwrite: true);
        }
    }

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            // Pooling would keep file handles alive after dispose, which blocks
            // file-level operations (and test cleanup) on Windows.
            Pooling = false
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);

    // Session ids come from the browser, so only accept the 32-hex "N" GUID shape we generate -
    // that rules out ".." and absolute paths before they ever reach the database.
    private static bool IsValidId(string? id) =>
        id is { Length: 32 } && id.All(Uri.IsHexDigit);
}
