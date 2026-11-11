using SQLAZOR.Models;
using System.Text.Json;

namespace SQLAZOR.Services;

public interface IAiSettingsStore
{
    /// <summary>Synchronous read of a small local file; used while the scoped state is constructed.</summary>
    AiSettingsSnapshot Load();

    Task SaveAsync(AiSettingsSnapshot snapshot, CancellationToken ct = default);
}

/// <summary>
/// Persists provider selection and per-provider connection settings (including API keys) to
/// %LOCALAPPDATA%\SQLAZOR\ai-settings.json. This file is intentionally separate from chat
/// sessions so exported conversations can never contain a key. Writes are atomic.
/// </summary>
public sealed class FileAiSettingsStore : IAiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public FileAiSettingsStore() : this(DefaultPath())
    {
    }

    public FileAiSettingsStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath() =>
        Path.Combine(AppDataPath.BaseDirectory(), "SQLAZOR", "ai-settings.json");

    public AiSettingsSnapshot Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new AiSettingsSnapshot();

            var snapshot = JsonSerializer.Deserialize<AiSettingsSnapshot>(
                File.ReadAllText(_path), JsonOptions);
            if (snapshot is null)
                return new AiSettingsSnapshot();

            snapshot.Configs ??= new(StringComparer.OrdinalIgnoreCase);
            return snapshot;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt settings file must not stop the app - fall back to defaults.
            return new AiSettingsSnapshot();
        }
    }

    public async Task SaveAsync(AiSettingsSnapshot snapshot, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, JsonOptions), ct);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
