using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Converts between the live <see cref="GenerationState"/> and a storable <see cref="GenerationProfile"/>.
/// Applying never carries credentials across — only selections, names, and options.
/// </summary>
public static class GenerationProfileMapper
{
    public static GenerationProfile Capture(GenerationState state, string name, GenerationProfile? existing = null)
    {
        var profile = existing ?? new GenerationProfile { CreatedAt = DateTimeOffset.Now };

        profile.Name = string.IsNullOrWhiteSpace(name) ? "New profile" : name.Trim();
        profile.DatabaseKey = state.Conn.DatabaseKey;
        profile.DatabaseName = state.Conn.Database;

        profile.RootNamespace = state.RootNamespace;
        profile.DbContextName = state.DbContextName;
        profile.ApplicationName = state.ApplicationName;

        profile.TableKeys = Sorted(state.SelectedTableKeys);
        profile.ViewKeys = Sorted(state.SelectedViewKeys);
        profile.ProcedureKeys = Sorted(state.SelectedProcedureKeys);

        profile.Options = Copy(state.Options);
        profile.AcceptedInsightTitles = Sorted(state.AcceptedInsightTitles);
        profile.LookupSuggestions = state.LookupSuggestions.Select(CloneLookup).ToList();
        profile.AcceptedLookupKeys = Sorted(state.AcceptedLookupKeys);

        profile.UpdatedAt = DateTimeOffset.Now;
        return profile;
    }

    /// <summary>
    /// Applies a profile to the current state. When a schema is loaded, keys that no longer exist
    /// are dropped and reported rather than silently generating nothing for them.
    /// </summary>
    public static GenerationProfileApplyResult Apply(GenerationProfile profile, GenerationState state)
    {
        var knownTables = KnownKeys(state, isView: false);
        var knownViews = KnownKeys(state, isView: true);
        // An empty list means procedures were never read, not that none exist - don't drop keys.
        var knownProcedures = state.Procedures.Count == 0
            ? null
            : state.Procedures.Select(p => p.FullyQualifiedName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tables = Filter(profile.TableKeys, knownTables);
        var views = Filter(profile.ViewKeys, knownViews);
        var procedures = Filter(profile.ProcedureKeys, knownProcedures);

        state.SelectedTableKeys = tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.SelectedViewKeys = views.ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.SelectedProcedureKeys = procedures.ToHashSet(StringComparer.OrdinalIgnoreCase);

        state.RootNamespace = string.IsNullOrWhiteSpace(profile.RootNamespace) ? "MyApp.Data" : profile.RootNamespace.Trim();
        state.DbContextName = string.IsNullOrWhiteSpace(profile.DbContextName) ? "AppDbContext" : profile.DbContextName.Trim();
        state.ApplicationName = string.IsNullOrWhiteSpace(profile.ApplicationName) ? "GeneratedApp" : profile.ApplicationName.Trim();
        state.Options = Normalize(profile.Options);

        var suggestedTitles = state.DashboardInsights.Select(i => i.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appliedTitles = profile.AcceptedInsightTitles.Where(suggestedTitles.Contains).ToList();
        state.AcceptedInsightTitles = appliedTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        state.LookupSuggestions = profile.LookupSuggestions.Select(CloneLookup).ToList();
        var suggestionKeys = state.LookupSuggestions.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.AcceptedLookupKeys = profile.AcceptedLookupKeys
            .Where(suggestionKeys.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.NotifyStateChanged();

        return new GenerationProfileApplyResult
        {
            MissingTableKeys = Missing(profile.TableKeys, knownTables),
            MissingViewKeys = Missing(profile.ViewKeys, knownViews),
            MissingProcedureKeys = Missing(profile.ProcedureKeys, knownProcedures),
            AppliedInsightTitleCount = appliedTitles.Count
        };
    }

    private static HashSet<string>? KnownKeys(GenerationState state, bool isView) =>
        state.Schema?.Tables
            .Where(t => t.IsView == isView)
            .Select(t => t.FullyQualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<string> Filter(IEnumerable<string>? keys, HashSet<string>? known)
    {
        if (keys is null)
            return [];

        var distinct = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase);
        return known is null ? distinct.ToList() : distinct.Where(known.Contains).ToList();
    }

    private static List<string> Missing(IEnumerable<string>? keys, HashSet<string>? known) =>
        keys is null || known is null
            ? []
            : keys.Where(k => !known.Contains(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> Sorted(IEnumerable<string> keys) =>
        keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    private static LookupSuggestion CloneLookup(LookupSuggestion source) => new()
    {
        TableKey = source.TableKey,
        ColumnName = source.ColumnName,
        ReferencedTableKey = source.ReferencedTableKey,
        ValueColumn = source.ValueColumn,
        LabelColumns = [.. source.LabelColumns],
        Options = [.. source.Options],
        Reason = source.Reason
    };

    private static GenerationOptions Copy(GenerationOptions source) => new()
    {
        GenerateCrudServices = source.GenerateCrudServices,
        GenerateApiEndpoints = source.GenerateApiEndpoints,
        GenerateHttpClientServices = source.GenerateHttpClientServices,
        GenerateBlazorPages = source.GenerateBlazorPages,
        PageStyle = source.PageStyle,
        GenerateProjectScaffold = source.GenerateProjectScaffold,
        GenerateDocumentation = source.GenerateDocumentation,
        UseCleanArchitecture = source.UseCleanArchitecture
    };

    /// <summary>Re-establishes the option dependencies enforced by the Generate tab's checkboxes.</summary>
    private static GenerationOptions Normalize(GenerationOptions? source)
    {
        var options = Copy(source ?? new GenerationOptions());

        if (!options.GenerateCrudServices)
        {
            options.GenerateApiEndpoints = false;
            options.GenerateHttpClientServices = false;
            options.GenerateBlazorPages = false;
        }

        if (!options.GenerateApiEndpoints)
            options.GenerateHttpClientServices = false;

        if (!options.GenerateBlazorPages)
            options.PageStyle = PageStyle.Plain;
        else if (!Enum.IsDefined(options.PageStyle))
            options.PageStyle = PageStyle.Plain;

        if (!options.GenerateProjectScaffold)
            options.UseCleanArchitecture = false;

        return options;
    }
}
