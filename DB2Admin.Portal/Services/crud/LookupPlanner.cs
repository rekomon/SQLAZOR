using SQLAZOR.Models;

namespace SQLAZOR.Services;

public enum LookupValueSource
{
    /// <summary>Option value is copied from a column on the referenced table (usually its key).</summary>
    ReferencedColumn,

    /// <summary>Text column: the option's display text is also the stored value (e.g. CustomerName).</summary>
    DisplayText,

    /// <summary>Fixed list supplied by the user/AI; no reference table involved.</summary>
    StaticList
}

/// <summary>One column rendered as a dropdown, with everything the Razor generator needs.</summary>
public sealed class ColumnLookup
{
    public required ColumnInfo Column { get; init; }
    public TableInfo? ReferencedTable { get; init; }
    public ColumnInfo? ValueColumn { get; init; }
    public List<ColumnInfo> LabelColumns { get; init; } = [];
    public List<string> StaticOptions { get; init; } = [];
    public LookupValueSource ValueSource { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class TableLookupPlan
{
    public required TableInfo Table { get; init; }
    public List<ColumnLookup> Columns { get; init; } = [];

    public ColumnLookup? ForColumn(string columnName) =>
        Columns.FirstOrDefault(c => c.Column.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));
}

public sealed class LookupPlan
{
    public Dictionary<string, TableLookupPlan> ByTable { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rejected or adjusted AI suggestions, surfaced as generation warnings.</summary>
    public List<string> Warnings { get; init; } = [];

    public TableLookupPlan? ForTable(TableInfo table) =>
        ByTable.TryGetValue(table.FullyQualifiedName, out var plan) ? plan : null;
}

/// <summary>
/// Builds the per-table lookup plan used by the Blazor page generator. Real foreign keys (which
/// include accepted implicit relationships, since those are synthesized into the effective schema)
/// are always turned into dropdowns. AI suggestions are layered on top for columns that have no FK:
/// text columns that copy a value from another table, and categorical columns with a fixed list of
/// choices. Suggestions that reference unselected tables or incompatible columns are rejected with
/// a warning instead of producing broken code.
/// </summary>
public static class LookupPlanner
{
    private static readonly string[] PreferredLabelNames =
        ["Name", "FullName", "DisplayName", "Title", "Label", "Subject", "Code", "Sku", "Description"];

    private static readonly string[] FirstNameColumns =
        ["FirstName", "GivenName", "First", "Forename"];

    private static readonly string[] LastNameColumns =
        ["LastName", "Surname", "FamilyName", "Last"];

    public static LookupPlan Build(
        DatabaseSchema schema,
        IEnumerable<string> selectedTableKeys,
        IEnumerable<LookupSuggestion>? inferredSuggestions = null)
    {
        var scope = selectedTableKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tables = schema.Tables.Where(t => scope.Contains(t.FullyQualifiedName) && !t.IsView).ToList();
        var plans = tables.ToDictionary(
            t => t.FullyQualifiedName,
            t => new TableLookupPlan { Table = t },
            StringComparer.OrdinalIgnoreCase);

        var warnings = new List<string>();

        foreach (var fk in schema.ForeignKeys)
        {
            var parentKey = $"{fk.ParentSchema}.{fk.ParentTable}";
            if (!plans.TryGetValue(parentKey, out var plan))
                continue;

            var referenced = tables.FirstOrDefault(t =>
                t.Schema.Equals(fk.ReferencedSchema, StringComparison.OrdinalIgnoreCase) &&
                t.TableName.Equals(fk.ReferencedTable, StringComparison.OrdinalIgnoreCase));
            if (referenced is null)
            {
                // Options can only come from a table that gets a generated service. Say so, otherwise
                // the column silently falls back to a plain input and looks like the feature failed.
                var orphanColumn = plan.Table.Columns.FirstOrDefault(c =>
                    c.ColumnName.Equals(fk.ParentColumn, StringComparison.OrdinalIgnoreCase));
                if (orphanColumn is not null && !orphanColumn.IsIdentity && !orphanColumn.IsComputed)
                {
                    warnings.Add(
                        $"'{plan.Table.TableName}.{orphanColumn.ColumnName}' stays a plain input: {fk.ReferencedSchema}.{fk.ReferencedTable} isn't selected.");
                }

                continue;
            }

            var parentColumn = plan.Table.Columns.FirstOrDefault(c =>
                c.ColumnName.Equals(fk.ParentColumn, StringComparison.OrdinalIgnoreCase));
            var valueColumn = referenced.Columns.FirstOrDefault(c =>
                c.ColumnName.Equals(fk.ReferencedColumn, StringComparison.OrdinalIgnoreCase));

            if (parentColumn is null || valueColumn is null)
                continue;
            if (!ClrTypesMatch(parentColumn.ClrType, valueColumn.ClrType))
                continue;
            if (plan.ForColumn(parentColumn.ColumnName) is not null)
                continue;

            plan.Columns.Add(new ColumnLookup
            {
                Column = parentColumn,
                ReferencedTable = referenced,
                ValueColumn = valueColumn,
                LabelColumns = PickLabelColumns(referenced, valueColumn),
                ValueSource = LookupValueSource.ReferencedColumn,
                Reason = $"Foreign key to {referenced.Schema}.{referenced.TableName}."
            });
        }

        foreach (var suggestion in inferredSuggestions ?? [])
        {
            ApplySuggestion(schema, scope, plans, suggestion, warnings);
        }

        return new LookupPlan { ByTable = plans, Warnings = warnings };
    }

    private static void ApplySuggestion(
        DatabaseSchema schema,
        HashSet<string> scope,
        Dictionary<string, TableLookupPlan> plans,
        LookupSuggestion suggestion,
        List<string> warnings)
    {
        var parent = ResolveTable(schema, scope, suggestion.TableKey);
        if (parent is null || !plans.TryGetValue(parent.FullyQualifiedName, out var plan))
        {
            warnings.Add($"Lookup '{suggestion.TableKey}.{suggestion.ColumnName}' skipped: the table isn't selected.");
            return;
        }

        var column = parent.Columns.FirstOrDefault(c =>
            c.ColumnName.Equals(suggestion.ColumnName, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            warnings.Add($"Lookup '{parent.TableName}.{suggestion.ColumnName}' skipped: column not found.");
            return;
        }
        if (column.IsIdentity || column.IsComputed)
        {
            warnings.Add($"Lookup '{parent.TableName}.{column.ColumnName}' skipped: identity/computed columns aren't editable.");
            return;
        }
        if (plan.ForColumn(column.ColumnName) is not null)
        {
            warnings.Add($"Lookup '{parent.TableName}.{column.ColumnName}' was ignored: it already has a foreign-key dropdown.");
            return;
        }

        var label = $"{parent.TableName}.{column.ColumnName}";

        if (suggestion.Kind == LookupSuggestionKind.Static)
        {
            if (BaseClrType(column.ClrType) != "string")
            {
                warnings.Add($"Lookup '{label}' skipped: fixed choices only apply to text columns.");
                return;
            }

            var options = suggestion.Options
                .Select(o => o?.Trim() ?? "")
                .Where(o => o.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();

            if (options.Count < 2)
            {
                warnings.Add($"Lookup '{label}' skipped: a fixed list needs at least two values.");
                return;
            }

            plan.Columns.Add(new ColumnLookup
            {
                Column = column,
                StaticOptions = options,
                ValueSource = LookupValueSource.StaticList,
                Reason = suggestion.Reason ?? "Fixed value list."
            });
            return;
        }

        var referenced = ResolveTable(schema, scope, suggestion.ReferencedTableKey);
        if (referenced is null)
        {
            warnings.Add($"Lookup '{label}' skipped: reference table '{suggestion.ReferencedTableKey}' isn't selected.");
            return;
        }

        ColumnInfo? valueColumn = null;
        var valueSource = LookupValueSource.DisplayText;

        if (!string.IsNullOrWhiteSpace(suggestion.ValueColumn))
        {
            valueColumn = referenced.Columns.FirstOrDefault(c =>
                c.ColumnName.Equals(suggestion.ValueColumn, StringComparison.OrdinalIgnoreCase));
            if (valueColumn is null)
            {
                warnings.Add($"Lookup '{label}' skipped: column '{suggestion.ValueColumn}' doesn't exist on {referenced.TableName}.");
                return;
            }
            if (!ClrTypesMatch(column.ClrType, valueColumn.ClrType))
            {
                warnings.Add($"Lookup '{label}' skipped: {referenced.TableName}.{valueColumn.ColumnName} doesn't match the column's type.");
                return;
            }

            valueSource = LookupValueSource.ReferencedColumn;
        }
        else if (BaseClrType(column.ClrType) != "string")
        {
            warnings.Add($"Lookup '{label}' skipped: a non-text column needs a value column on {referenced.TableName}.");
            return;
        }

        var labelColumns = suggestion.LabelColumns
            .Select(name => referenced.Columns.FirstOrDefault(c =>
                c.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Where(c => c is not null)
            .Select(c => c!)
            .DistinctBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (labelColumns.Count == 0)
            labelColumns = PickLabelColumns(referenced, valueColumn);

        if (labelColumns.Count == 0)
        {
            warnings.Add($"Lookup '{label}' skipped: no display column found on {referenced.TableName}.");
            return;
        }

        plan.Columns.Add(new ColumnLookup
        {
            Column = column,
            ReferencedTable = referenced,
            ValueColumn = valueColumn,
            LabelColumns = labelColumns,
            ValueSource = valueSource,
            Reason = suggestion.Reason ??
                (valueSource == LookupValueSource.DisplayText
                    ? $"Text value sourced from {referenced.Schema}.{referenced.TableName}."
                    : $"Reference to {referenced.Schema}.{referenced.TableName}.{valueColumn!.ColumnName}.")
        });
    }

    /// <summary>Best guess at the column(s) a human would use to identify a row.</summary>
    public static List<ColumnInfo> PickLabelColumns(TableInfo table, ColumnInfo? valueColumn = null)
    {
        var textColumns = table.Columns
            .Where(c => !c.IsComputed && BaseClrType(c.ClrType) == "string")
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        foreach (var preferred in PreferredLabelNames)
        {
            var match = textColumns.FirstOrDefault(c =>
                c.ColumnName.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return [match];
        }

        var first = textColumns.FirstOrDefault(c =>
            FirstNameColumns.Any(n => c.ColumnName.Equals(n, StringComparison.OrdinalIgnoreCase)));
        var last = textColumns.FirstOrDefault(c =>
            LastNameColumns.Any(n => c.ColumnName.Equals(n, StringComparison.OrdinalIgnoreCase)));
        if (first is not null && last is not null)
            return [first, last];
        if (first is not null)
            return [first];

        if (valueColumn is not null)
            return [valueColumn];

        var pk = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (pk is not null)
            return [pk];

        return textColumns.Take(1).ToList();
    }

    private static TableInfo? ResolveTable(DatabaseSchema schema, HashSet<string> scope, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var trimmed = key.Trim();
        var qualified = schema.Tables.FirstOrDefault(t =>
            !t.IsView &&
            t.FullyQualifiedName.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (qualified is not null)
            return scope.Contains(qualified.FullyQualifiedName) ? qualified : null;

        var byName = schema.Tables
            .Where(t => !t.IsView && t.TableName.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byName.Count == 1 && scope.Contains(byName[0].FullyQualifiedName) ? byName[0] : null;
    }

    public static string BaseClrType(string clrType) => clrType.TrimEnd('?');

    private static bool ClrTypesMatch(string a, string b) =>
        BaseClrType(a).Equals(BaseClrType(b), StringComparison.Ordinal);
}
