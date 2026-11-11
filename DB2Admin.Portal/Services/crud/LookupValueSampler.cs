using Microsoft.Data.SqlClient;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Proposes fixed-choice lookups by reading DISTINCT values straight from the database.
/// Read-only by construction (a single SELECT per candidate), bounded by TOP, and the values
/// never leave the machine - they are not sent to an AI provider.
/// </summary>
public sealed class LookupValueSampler
{
    /// <summary>A list longer than this is data, not a category, and is skipped.</summary>
    public const int MaxAcceptedValues = 10;
    public const int ProbeLimit = MaxAcceptedValues + 1;
    public const int CommandTimeoutSeconds = 15;

    private static readonly string[] CategoricalNames =
    [
        "status", "state", "type", "category", "kind", "priority", "severity",
        "role", "plan", "tier", "level", "stage", "method", "mode", "group", "currency"
    ];

    public static bool IsCategorical(ColumnInfo column)
    {
        if (column.ClrType.TrimEnd('?') != "string" || column.IsIdentity || column.IsComputed)
            return false;

        return CategoricalNames.Any(word => MatchesName(column.ColumnName, word));
    }

    /// <summary>Builds the read-only probe for one column; identifiers are bracketed and escaped.</summary>
    public static string BuildSql(TableInfo table, ColumnInfo column)
    {
        var qualifiedTable = $"{Quote(table.Schema)}.{Quote(table.TableName)}";
        var quotedColumn = Quote(column.ColumnName);
        return $"SELECT DISTINCT TOP ({ProbeLimit}) {quotedColumn} FROM {qualifiedTable} " +
               $"WHERE {quotedColumn} IS NOT NULL AND LTRIM(RTRIM({quotedColumn})) <> '' " +
               $"ORDER BY {quotedColumn}";
    }

    public async Task<(List<LookupSuggestion> Suggestions, List<string> Warnings)> SampleAsync(
        DatabaseSchema schema,
        IEnumerable<string> selectedTableKeys,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var suggestions = new List<LookupSuggestion>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            warnings.Add("Connect to a database before scanning distinct values.");
            return (suggestions, warnings);
        }

        var scope = selectedTableKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = schema.Tables
            .Where(t => !t.IsView && scope.Contains(t.FullyQualifiedName))
            .SelectMany(t => t.Columns.Where(IsCategorical).Select(c => (Table: t, Column: c)))
            .ToList();

        if (candidates.Count == 0)
            return (suggestions, warnings);

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            foreach (var (table, column) in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var values = new List<string>();
                await using var command = new SqlCommand(BuildSql(table, column), connection)
                {
                    CommandTimeout = CommandTimeoutSeconds
                };

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                        values.Add(reader.GetString(0).Trim());
                }

                var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // ProbeLimit rows means the real cardinality is higher than a lookup should hold.
                if (distinct.Count < 2 || distinct.Count > MaxAcceptedValues)
                    continue;

                suggestions.Add(new LookupSuggestion
                {
                    TableKey = table.FullyQualifiedName,
                    ColumnName = column.ColumnName,
                    Options = distinct,
                    Reason = "Distinct values read from the database (local read-only query)."
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add($"Distinct-value scan stopped: {ex.Message}");
        }

        return (suggestions, warnings);
    }

    private static bool MatchesName(string name, string word)
    {
        if (name.Equals(word, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!name.EndsWith(word, StringComparison.OrdinalIgnoreCase) || name.Length == word.Length)
            return false;

        var wordStart = name.Length - word.Length;
        var previous = name[wordStart - 1];
        return char.IsUpper(name[wordStart]) || previous is '_' or '-' or ' ';
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}

/// <summary>Ordering rules for combining AI suggestions with values scanned from the database.</summary>
public static class LookupSuggestionSet
{
    /// <summary>
    /// Merges lists by table+column. Later lists win, so scanned values replace AI guesses for the
    /// same column while unrelated suggestions are preserved.
    /// </summary>
    public static List<LookupSuggestion> Merge(
        IEnumerable<LookupSuggestion> existing,
        IEnumerable<LookupSuggestion> incoming)
    {
        var byKey = new Dictionary<string, LookupSuggestion>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var suggestion in existing.Concat(incoming))
        {
            if (!byKey.ContainsKey(suggestion.Key))
                order.Add(suggestion.Key);

            byKey[suggestion.Key] = suggestion;
        }

        return order.Select(key => byKey[key]).ToList();
    }
}
