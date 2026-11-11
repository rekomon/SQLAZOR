namespace SQLAZOR.Models;

public enum LookupSuggestionKind { Reference, Static }

/// <summary>
/// A column that should be edited through a dropdown instead of a free-text input. Reference
/// lookups pull their options from another table; static lookups use a short fixed list (e.g. a
/// Status column with no reference table). Suggestions are produced by the AI assistant and are
/// only applied after the user ticks them.
/// </summary>
public sealed class LookupSuggestion
{
    /// <summary>Fully-qualified parent table, e.g. "dbo.Tickets".</summary>
    public required string TableKey { get; init; }
    public required string ColumnName { get; init; }

    /// <summary>Fully-qualified source table for reference lookups; null for static lists.</summary>
    public string? ReferencedTableKey { get; init; }

    /// <summary>
    /// Column whose value is stored in the parent column. Null for text columns that store the
    /// displayed label itself (e.g. Tickets.CustomerName copied from Customers.FirstName + LastName).
    /// </summary>
    public string? ValueColumn { get; init; }

    /// <summary>Column(s) rendered as the dropdown option text.</summary>
    public List<string> LabelColumns { get; init; } = [];

    /// <summary>Fixed choices for static lookups (Status, Priority, ...).</summary>
    public List<string> Options { get; init; } = [];

    public string? Reason { get; init; }

    public LookupSuggestionKind Kind => Options.Count > 0 ? LookupSuggestionKind.Static : LookupSuggestionKind.Reference;

    public string Key => $"{TableKey}.{ColumnName}";
}
