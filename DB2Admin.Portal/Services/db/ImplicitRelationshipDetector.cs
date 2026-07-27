using SQLAZOR.Models;
using SQLAZOR.Services;

namespace SQLAZOR.Services;

/// <summary>
/// Flags columns that look like foreign keys by naming convention (ends in "Id"/"Code" and
/// matches another table's name) but have no real FK constraint in the database — common in
/// older schemas where relationships were never formally declared. This is a deterministic
/// naming + type-compatibility heuristic, not an AI call: it's instant, free, and reproducible,
/// which matters more here than a language model's guess at table semantics.
/// </summary>
public static class ImplicitRelationshipDetector
{
    public static List<ImplicitForeignKeyCandidate> Detect(DatabaseSchema schema, IEnumerable<string> tableKeysInScope)
    {
        var scope = tableKeysInScope.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tables = schema.Tables.Where(t => scope.Contains(t.FullyQualifiedName) && !t.IsView).ToList();

        var existingFkColumns = schema.ForeignKeys
            .Select(fk => $"{fk.ParentSchema}.{fk.ParentTable}.{fk.ParentColumn}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Index tables by singular class name and by raw table name for matching.
        var tablesByClassName = tables
            .GroupBy(t => t.ClassName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1) // skip ambiguous cases (two tables singularizing to the same name)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var candidates = new List<ImplicitForeignKeyCandidate>();

        foreach (var table in tables)
        {
            foreach (var col in table.Columns.OrderBy(c => c.OrdinalPosition))
            {
                var columnKey = $"{table.Schema}.{table.TableName}.{col.ColumnName}";
                if (existingFkColumns.Contains(columnKey))
                    continue; // already a real FK, nothing implicit about it

                if (col.IsPrimaryKey && table.PrimaryKeyColumns.Count == 1)
                    continue; // a lone PK column is an identity, not a reference (self-referencing PK-as-FK is rare enough to skip)

                var baseName = StripIdSuffix(col.ColumnName);
                if (baseName is null)
                    continue; // doesn't look like a *Id / *ID / *_id column at all

                var pascalBase = NamingHelper.ToPascalCase(baseName);

                if (!tablesByClassName.TryGetValue(pascalBase, out var targetTable))
                    continue; // no table name matches this column's implied entity

                if (targetTable.PrimaryKeyColumns.Count != 1)
                    continue; // only handle simple single-column PK targets

                var targetPkColumn = targetTable.Columns.First(c => c.IsPrimaryKey);

                // Sanity-check the types are at least compatible (both integer-ish, both Guid, etc.)
                if (!TypesAreCompatible(col.SqlDataType, targetPkColumn.SqlDataType))
                    continue;

                var isExactSelfMatch = targetTable.FullyQualifiedName.Equals(table.FullyQualifiedName, StringComparison.OrdinalIgnoreCase);
                var confidence = pascalBase.Equals(targetTable.ClassName, StringComparison.Ordinal)
                    ? ImplicitFkConfidence.High
                    : ImplicitFkConfidence.Medium;

                var reason = isExactSelfMatch
                    ? $"'{col.ColumnName}' looks like a self-reference to {table.TableName}'s own key ('{StripIdSuffix(col.ColumnName)}' matches this table)."
                    : $"'{col.ColumnName}' matches table '{targetTable.TableName}' by name, and both keys are {col.SqlDataType}-compatible.";

                candidates.Add(new ImplicitForeignKeyCandidate
                {
                    ParentSchema = table.Schema,
                    ParentTable = table.TableName,
                    ParentColumn = col.ColumnName,
                    ReferencedSchema = targetTable.Schema,
                    ReferencedTable = targetTable.TableName,
                    ReferencedColumn = targetPkColumn.ColumnName,
                    Confidence = confidence,
                    Reason = reason
                });
            }
        }

        return candidates;
    }

    /// <summary>Strips a trailing Id/ID/_Id/_id suffix and returns the base name, or null if there wasn't one.</summary>
    private static string? StripIdSuffix(string columnName)
    {
        foreach (var suffix in new[] { "_Id", "_ID", "_id", "Id", "ID" })
        {
            if (columnName.EndsWith(suffix, StringComparison.Ordinal) && columnName.Length > suffix.Length)
            {
                return columnName[..^suffix.Length];
            }
        }

        return null;
    }

    private static bool TypesAreCompatible(string sqlTypeA, string sqlTypeB)
    {
        var a = NormalizeTypeFamily(sqlTypeA);
        var b = NormalizeTypeFamily(sqlTypeB);
        return a == b;
    }

    private static string NormalizeTypeFamily(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "int" or "bigint" or "smallint" or "tinyint" => "integer",
        "uniqueidentifier" => "guid",
        "varchar" or "nvarchar" or "char" or "nchar" => "string",
        _ => sqlType.ToLowerInvariant()
    };
}
