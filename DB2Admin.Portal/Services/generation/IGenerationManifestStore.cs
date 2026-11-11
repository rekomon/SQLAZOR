using SQLAZOR.Models;

namespace SQLAZOR.Services;

public interface IGenerationManifestStore
{
    Task<GenerationManifest?> LoadAsync(string databaseKey, CancellationToken ct = default);
    Task SaveAsync(GenerationManifest manifest, CancellationToken ct = default);
    Task DeleteAsync(string databaseKey, CancellationToken ct = default);
}

/// <summary>
/// One JSON file per database under %LOCALAPPDATA%\SQLAZOR\generation-manifests.
/// The file name is a hash of the server+database key, so no connection details appear on disk.
/// </summary>
public sealed class FileGenerationManifestStore : IGenerationManifestStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public FileGenerationManifestStore() : this(DefaultDirectory())
    {
    }

    public FileGenerationManifestStore(string directory)
    {
        _directory = directory;
    }

    public static string DefaultDirectory() =>
        Path.Combine(AppDataPath.BaseDirectory(), "SQLAZOR", "generation-manifests");

    public async Task<GenerationManifest?> LoadAsync(string databaseKey, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var path = PathFor(databaseKey);
            if (!File.Exists(path))
                return null;

            var json = await File.ReadAllTextAsync(path, ct);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<GenerationManifest>(json);
            if (manifest is null || !string.Equals(manifest.DatabaseKey, databaseKey, StringComparison.OrdinalIgnoreCase))
                return null;

            manifest.Files ??= [];
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(GenerationManifest manifest, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_directory);

            var path = PathFor(manifest.DatabaseKey);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, System.Text.Json.JsonSerializer.Serialize(manifest, JsonOptions), ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string databaseKey, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var path = PathFor(databaseKey);
            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string PathFor(string databaseKey) =>
        Path.Combine(_directory, ContentHash.Compute(databaseKey).ToLowerInvariant() + ".json");

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
