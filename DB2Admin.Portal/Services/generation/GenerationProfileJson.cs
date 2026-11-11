using System.Text.Json;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>Export/import format for generation profiles. One profile per file, human-editable.</summary>
public static class GenerationProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(GenerationProfile profile) =>
        JsonSerializer.Serialize(profile, Options);

    /// <summary>
    /// Parses an imported profile. Returns null for anything that isn't a usable profile, and
    /// always assigns a fresh id so importing the same file twice creates two entries.
    /// </summary>
    public static GenerationProfile? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        GenerationProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<GenerationProfile>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (profile is null)
            return null;

        profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "Imported profile" : profile.Name.Trim();
        profile.Id = Guid.NewGuid().ToString("N");
        profile.TableKeys ??= [];
        profile.ViewKeys ??= [];
        profile.ProcedureKeys ??= [];
        profile.AcceptedInsightTitles ??= [];
        profile.LookupSuggestions ??= [];
        profile.AcceptedLookupKeys ??= [];
        profile.Options ??= new GenerationOptions();
        profile.CreatedAt = DateTimeOffset.Now;
        profile.UpdatedAt = DateTimeOffset.Now;

        return profile;
    }
}
