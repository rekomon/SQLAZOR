using System.Text;
using System.Text.Json;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

public static class NamingAssistant
{
    private const string PromptTemplate = @"You are reviewing an entity class written in C#, automatically created from a table in SQL Server by converting the raw column names to PascalCase. Your task is to suggest clearer names only in cases where the code name is unclear (vague abbreviations, or incomprehensible acronyms) – leave obvious names as they are. Important note: Do not suggest names similar to the class name.

Also, write a brief, one-line summary, similar to an XML-doc, for the class and for any property whose purpose is not clear from its name alone (leave the 'summary' value blank for obvious properties like `Id`, `Name`, and `Email`).

Table: {0}.{1}
Columns (Name: SQL type, accepts null values?):
{2}

Answer with only valid JSON data, without Markdown, and in exactly the following format:
{{
""""className"""": ""String - PascalCase, leave as is unless it is completely obvious"",
""""classSummary"""": ""String or null value - one line describing what this table represents"",
""""properties"""": [
{{ ""column"": ""Original column name"", ""propertyName"": ""PascalCase suggestion, same format if obvious"", ""summary"": ""String or null value"" }}

]
}}
List each of the above columns, in the same order, only once.";

    public static string BuildPrompt(TableInfo table)
    {
        var columnLines = new StringBuilder();
        foreach (var col in table.Columns.OrderBy(c => c.OrdinalPosition))
        {
            columnLines.AppendLine($"- {col.ColumnName}: {col.SqlDataType}{(col.IsNullable ? ", nullable" : "")}");
        }

        return string.Format(PromptTemplate, table.Schema, table.TableName, columnLines.ToString().TrimEnd());
    }

    /// <summary>Parses the model's JSON reply. Returns null (rather than throwing) on malformed output
    /// so a single bad response doesn't break a multi-table batch.</summary>
    public static TableNamingSuggestion? TryParse(string tableKey, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var className = root.TryGetProperty("className", out var classNameEl) ? classNameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(className))
                return null;

            var classSummary = root.TryGetProperty("classSummary", out var summaryEl) && summaryEl.ValueKind == JsonValueKind.String
                ? summaryEl.GetString()
                : null;

            var properties = new List<PropertyNamingSuggestion>();
            if (root.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var propEl in propsEl.EnumerateArray())
                {
                    var column = propEl.TryGetProperty("column", out var colEl) ? colEl.GetString() : null;
                    var propertyName = propEl.TryGetProperty("propertyName", out var propNameEl) ? propNameEl.GetString() : null;
                    var propSummary = propEl.TryGetProperty("summary", out var propSummaryEl) && propSummaryEl.ValueKind == JsonValueKind.String
                        ? propSummaryEl.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(propertyName))
                        continue;

                    properties.Add(new PropertyNamingSuggestion
                    {
                        ColumnName = column,
                        SuggestedPropertyName = propertyName,
                        Summary = propSummary
                    });
                }
            }

            return new TableNamingSuggestion
            {
                TableKey = tableKey,
                SuggestedClassName = className,
                ClassSummary = classSummary,
                Properties = properties
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
