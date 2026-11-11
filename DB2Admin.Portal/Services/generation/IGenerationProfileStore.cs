using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>Persists reusable generation profiles (selections + options) per database.</summary>
public interface IGenerationProfileStore
{
    Task<IReadOnlyList<GenerationProfileSummary>> ListAsync(string? databaseKey = null, CancellationToken ct = default);
    Task<GenerationProfile?> LoadAsync(string id, CancellationToken ct = default);
    Task SaveAsync(GenerationProfile profile, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
