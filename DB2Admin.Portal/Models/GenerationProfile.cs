namespace SQLAZOR.Models;

/// <summary>
/// A reusable snapshot of what to generate for one database: table/view/procedure selections,
/// naming, and generator options. Deliberately holds no server, user, or password values —
/// <see cref="DatabaseKey"/> is advisory metadata used to group profiles for the same database.
/// </summary>
public sealed class GenerationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string DatabaseKey { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = "MyApp.Data";
    public string DbContextName { get; set; } = "AppDbContext";
    public string ApplicationName { get; set; } = "GeneratedApp";

    public List<string> TableKeys { get; set; } = [];
    public List<string> ViewKeys { get; set; } = [];
    public List<string> ProcedureKeys { get; set; } = [];

    public GenerationOptions Options { get; set; } = new();

    /// <summary>Titles of accepted AI dashboard insights; re-applied only if the same title is suggested again.</summary>
    public List<string> AcceptedInsightTitles { get; set; } = [];

    /// <summary>AI/scanned lookup suggestions kept with the profile, plus the keys the user accepted.</summary>
    public List<LookupSuggestion> LookupSuggestions { get; set; } = [];
    public List<string> AcceptedLookupKeys { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class GenerationProfileSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string DatabaseKey { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public int TableCount { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>What applying a profile to the current schema actually did.</summary>
public sealed class GenerationProfileApplyResult
{
    public List<string> MissingTableKeys { get; init; } = [];
    public List<string> MissingViewKeys { get; init; } = [];
    public List<string> MissingProcedureKeys { get; init; } = [];
    public int AppliedInsightTitleCount { get; init; }

    public int MissingTotal => MissingTableKeys.Count + MissingViewKeys.Count + MissingProcedureKeys.Count;
}
