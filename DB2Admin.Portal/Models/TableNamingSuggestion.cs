namespace SQLAZOR.Models;

/// <summary>AI-suggested naming + documentation improvements for one table.</summary>
public sealed class TableNamingSuggestion
{
    public required string TableKey { get; init; } // schema.table, matches TableInfo.FullyQualifiedName
    public required string SuggestedClassName { get; init; }
    public string? ClassSummary { get; init; }
    public List<PropertyNamingSuggestion> Properties { get; init; } = [];

    /// <summary>Whether the original class name differs from the suggestion (nothing to apply otherwise).</summary>
    public bool HasClassNameChange(string originalClassName) =>
        !SuggestedClassName.Equals(originalClassName, StringComparison.Ordinal);
}

public sealed class PropertyNamingSuggestion
{
    public required string ColumnName { get; init; }
    public required string SuggestedPropertyName { get; init; }
    public string? Summary { get; init; }
}
