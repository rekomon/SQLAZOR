
using System.Text.Json;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

public static class DashboardInsightsAssistant
{
    private const string PromptTemplate = @"You are suggesting dashboard charts for an admin panel built on the SQL Server
schema below. Suggest up to {1} genuinely useful charts - things a business user would actually want
to glance at (counts by category, trends over time if there's a date column, totals/averages by
group). Skip tables that don't have anything chart-worthy (pure lookup tables, junction tables).

Every suggestion MUST be a single read-only T-SQL SELECT statement (no WITH/CTE, no comments,
semicolons, or multiple statements) that returns EXACTLY two columns in this order: a text/date
label column first, then a single numeric aggregate value column second. Use TOP 20 to keep result
sets small. Use real table/column names from the schema below exactly as given, fully qualified
with schema (e.g. [dbo].[Orders]).

Schema:
{0}

Respond with ONLY valid JSON, no markdown fences, matching exactly this shape:
{{
  ""insights"": [
    {{
      ""title"": ""short chart title, e.g. 'Orders by Status'"",
      ""chartType"": ""bar"" | ""pie"" | ""line"",
      ""sql"": ""SELECT ... (single statement, two columns, TOP 20)"",
      ""description"": ""one line explaining why this is useful""
    }}
  ]
}}";

    public static string BuildPrompt(DatabaseSchema schema, List<StoredProcedureSummary>? procedures, int maxInsights)
    {
        var schemaContext = SchemaContextBuilder.Build(schema, procedures);
        return string.Format(PromptTemplate, schemaContext, maxInsights);
    }

    /// <summary>Parses the model's JSON reply. Malformed entries are skipped rather than aborting the whole batch.</summary>
    public static List<DashboardInsightCandidate> TryParse(string json)
    {
        var result = new List<DashboardInsightCandidate>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("insights", out var insightsEl) || insightsEl.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in insightsEl.EnumerateArray())
            {
                var title = el.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                var sql = el.TryGetProperty("sql", out var sqlEl) ? sqlEl.GetString() : null;
                var chartTypeText = el.TryGetProperty("chartType", out var chartTypeEl) ? chartTypeEl.GetString() : null;
                var description = el.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
                    ? descEl.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sql))
                    continue;

                var chartType = chartTypeText?.ToLowerInvariant() switch
                {
                    "pie" => InsightChartType.Pie,
                    "line" => InsightChartType.Line,
                    _ => InsightChartType.Bar
                };

                result.Add(new DashboardInsightCandidate
                {
                    Title = title.Trim(),
                    ChartType = chartType,
                    Sql = sql.Trim(),
                    Description = description
                });
            }
        }
        catch (JsonException)
        {
            // Return whatever we got (likely nothing) - caller shows "no suggestions" rather than an error.
        }

        return result;
    }
}
