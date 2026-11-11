using System.Text.Json;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Stores one JSON file per profile under %LOCALAPPDATA%\SQLAZOR\generation-profiles.
/// Same durability rules as the chat store: atomic temp+move writes, malformed files skipped.
/// </summary>
public sealed class FileGenerationProfileStore : IGenerationProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public FileGenerationProfileStore() : this(DefaultDirectory())
    {
    }

    public FileGenerationProfileStore(string directory)
    {
        _directory = directory;
    }

    public static string DefaultDirectory() =>
        Path.Combine(AppDataPath.BaseDirectory(), "SQLAZOR", "generation-profiles");

    public async Task<IReadOnlyList<GenerationProfileSummary>> ListAsync(
        string? databaseKey = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(_directory))
                return [];

            var summaries = new List<GenerationProfileSummary>();
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                ct.ThrowIfCancellationRequested();

                var profile = await ReadAsync(file, ct);
                if (profile is null)
                    continue;

                if (!string.IsNullOrWhiteSpace(databaseKey) &&
                    !string.Equals(profile.DatabaseKey, databaseKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                summaries.Add(new GenerationProfileSummary
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    DatabaseKey = profile.DatabaseKey,
                    DatabaseName = profile.DatabaseName,
                    TableCount = profile.TableKeys.Count,
                    UpdatedAt = profile.UpdatedAt
                });
            }

            return summaries
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GenerationProfile?> LoadAsync(string id, CancellationToken ct = default)
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

    public async Task SaveAsync(GenerationProfile profile, CancellationToken ct = default)
    {
        if (!IsValidId(profile.Id))
            profile.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(profile.Name))
            profile.Name = "New profile";

        profile.UpdatedAt = DateTimeOffset.Now;

        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_directory);

            var path = PathFor(profile.Id);
            var temp = path + ".tmp";
            var json = JsonSerializer.Serialize(profile, JsonOptions);

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

    private async Task<GenerationProfile?> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var profile = JsonSerializer.Deserialize<GenerationProfile>(json, JsonOptions);
            if (profile is null)
                return null;

            profile.TableKeys ??= [];
            profile.ViewKeys ??= [];
            profile.ProcedureKeys ??= [];
            profile.AcceptedInsightTitles ??= [];
            profile.Options ??= new GenerationOptions();
            profile.Id = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(profile.Name))
                profile.Name = "New profile";

            return profile;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string PathFor(string id) => Path.Combine(_directory, id + ".json");

    private static bool IsValidId(string? id) =>
        id is { Length: 32 } && id.All(Uri.IsHexDigit);
}
