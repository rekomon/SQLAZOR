using System.Text;
using System.Text.Json;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Asks the configured model to link text/categorical columns to reference tables so generated
/// admin forms can render dropdowns instead of free-text boxes. Only schema metadata (names and
/// types) is sent — never row data. Output is reviewed by the user before it affects generation.
/// </summary>
public static class LookupAssistant
{
    private const string PromptTemplate = """
        You are a senior database engineer configuring a Blazor admin generator.

        Tables in scope (schema.table: column:sqltype, PK = primary key):
        {0}

        Existing foreign keys (these already become dropdowns automatically):
        {1}

        Find columns that should be edited through a dropdown.

        Rules:
        - Only use tables and columns that appear above. Never invent names.
        - Skip primary keys, identity/computed columns, and columns already listed as foreign keys.
        - Skip free-text columns such as Issue, Device, Phone, Email, Notes, Description, Address, Comments.
        - REFERENCE lookup: the column always holds a value copied from another table
          (e.g. Tickets.CustomerName -> Customers, Tickets.Technician -> Staff).
          * Set "referencedTable" to that table.
          * If the column stores an id/code/number, set "valueColumn" to the referenced column that
            holds that value. If the column stores the human-readable name, leave "valueColumn" null
            so the displayed text is stored.
          * Set "labelColumns" to the column(s) a person would read (e.g. ["Name"] or
            ["FirstName","LastName"]), in display order.
        - STATIC lookup: a categorical text column with no matching table (e.g. Status, Priority,
          Category). Set "options" to 3-8 short conventional values. Only do this when the values are
          genuinely standard for the domain; otherwise omit the column.
        - A column is either a reference lookup or a static lookup, never both.
        - Prefer fewer, high-confidence suggestions over guessing.

        Answer with only valid JSON, no Markdown, in exactly this shape:
        {{
          "lookups": [
            {{
              "table": "dbo.Tickets",
              "column": "CustomerName",
              "referencedTable": "dbo.Customers",
              "valueColumn": null,
              "labelColumns": ["FirstName", "LastName"],
              "options": [],
              "reason": "One short sentence"
            }}
          ]
        }}
        """;

    public static string BuildPrompt(DatabaseSchema schema, IEnumerable<string> selectedTableKeys)
    {
        var scope = selectedTableKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tables = schema.Tables
            .Where(t => scope.Contains(t.FullyQualifiedName) && !t.IsView)
            .OrderBy(t => t.Schema)
            .ThenBy(t => t.TableName)
            .ToList();

        var tableLines = new StringBuilder();
        foreach (var table in tables)
        {
            var columns = table.Columns
                .OrderBy(c => c.OrdinalPosition)
                .Select(c =>
                {
                    var markers = new List<string>();
                    if (c.IsPrimaryKey) markers.Add("PK");
                    if (c.IsIdentity) markers.Add("identity");
                    if (c.IsComputed) markers.Add("computed");
                    var suffix = markers.Count > 0 ? $" [{string.Join(",", markers)}]" : "";
                    return $"{c.ColumnName}:{c.SqlDataType}{(c.IsNullable ? "?" : "")}{suffix}";
                });

            tableLines.AppendLine($"- {table.FullyQualifiedName}: {string.Join(", ", columns)}");
        }

        var fkLines = new StringBuilder();
        foreach (var fk in schema.ForeignKeys
            .Where(fk => scope.Contains($"{fk.ParentSchema}.{fk.ParentTable}"))
            .OrderBy(fk => fk.ParentTable)
            .ThenBy(fk => fk.ParentColumn))
        {
            fkLines.AppendLine($"- {fk.ParentSchema}.{fk.ParentTable}.{fk.ParentColumn} -> {fk.ReferencedSchema}.{fk.ReferencedTable}.{fk.ReferencedColumn}");
        }

        if (fkLines.Length == 0)
            fkLines.AppendLine("- (none)");

        return string.Format(PromptTemplate, tableLines.ToString().TrimEnd(), fkLines.ToString().TrimEnd());
    }

    /// <summary>
    /// Parses and sanitizes the model reply. Suggestions pointing at unknown tables/columns are
    /// dropped here; the planner performs the final type/reference validation.
    /// </summary>
    public static List<LookupSuggestion> Parse(string json, DatabaseSchema schema, IEnumerable<string> selectedTableKeys)
    {
        var scope = selectedTableKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<LookupSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(CleanJson(json));
        }
        catch (JsonException)
        {
            return result;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("lookups", out var lookups))
                root = lookups;

            if (root.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var table = ResolveTable(schema, scope, GetString(item, "table"));
                var columnName = GetString(item, "column");
                if (table is null || string.IsNullOrWhiteSpace(columnName))
                    continue;

                var column = table.Columns.FirstOrDefault(c =>
                    c.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                if (column is null)
                    continue;

                var options = GetStringArray(item, "options");
                var referencedTable = options.Count > 0
                    ? null
                    : ResolveTable(schema, scope, GetString(item, "referencedTable"));

                if (options.Count == 0 && referencedTable is null)
                    continue;

                var valueColumn = GetString(item, "valueColumn");
                if (valueColumn is not null && referencedTable is not null &&
                    referencedTable.Columns.All(c => !c.ColumnName.Equals(valueColumn, StringComparison.OrdinalIgnoreCase)))
                {
                    valueColumn = null;
                }

                var labelColumns = GetStringArray(item, "labelColumns");
                if (referencedTable is not null)
                {
                    labelColumns = labelColumns
                        .Where(name => referencedTable.Columns.Any(c =>
                            c.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }

                var key = $"{table.FullyQualifiedName}.{column.ColumnName}";
                if (!seen.Add(key))
                    continue;

                result.Add(new LookupSuggestion
                {
                    TableKey = table.FullyQualifiedName,
                    ColumnName = column.ColumnName,
                    ReferencedTableKey = referencedTable?.FullyQualifiedName,
                    ValueColumn = valueColumn,
                    LabelColumns = labelColumns,
                    Options = options,
                    Reason = GetString(item, "reason")
                });

                if (result.Count >= 200)
                    break;
            }
        }

        return result;
    }

    private static string CleanJson(string json)
    {
        var trimmed = json.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd < 0 || lastFence <= firstLineEnd)
            return trimmed;

        return trimmed[(firstLineEnd + 1)..lastFence].Trim();
    }

    private static TableInfo? ResolveTable(DatabaseSchema schema, HashSet<string> scope, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var trimmed = key.Trim();
        var qualified = schema.Tables.FirstOrDefault(t =>
            !t.IsView && t.FullyQualifiedName.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (qualified is not null)
            return scope.Contains(qualified.FullyQualifiedName) ? qualified : null;

        var byName = schema.Tables
            .Where(t => !t.IsView && t.TableName.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byName.Count == 1 && scope.Contains(byName[0].FullyQualifiedName) ? byName[0] : null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString().Trim()
        };
    }

    private static List<string> GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return [];

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString()?.Trim();
            return string.IsNullOrEmpty(single) ? [] : [single];
        }

        if (value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
