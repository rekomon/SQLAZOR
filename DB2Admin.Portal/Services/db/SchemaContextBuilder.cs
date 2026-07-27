using System.Text;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

public static class SchemaContextBuilder
{
    /// <summary>
    /// Renders tables (columns + types + PK/FK markers), relationships, and — optionally —
    /// stored procedure signatures into plain text compact enough to sit in a chat system prompt.
    /// </summary>
    public static string Build(DatabaseSchema schema, List<StoredProcedureSummary>? procedures = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Database: {schema.DatabaseName}");
        sb.AppendLine();
        sb.AppendLine("Tables:");

        foreach (var table in schema.Tables.OrderBy(t => t.Schema).ThenBy(t => t.TableName))
        {
            var pkNames = table.PrimaryKeyColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fkByColumn = schema.ForeignKeys
                .Where(fk => fk.ParentSchema == table.Schema && fk.ParentTable == table.TableName)
                .ToDictionary(fk => fk.ParentColumn, fk => $"{fk.ReferencedSchema}.{fk.ReferencedTable}.{fk.ReferencedColumn}", StringComparer.OrdinalIgnoreCase);

            sb.Append($"- {table.Schema}.{table.TableName}");
            if (table.IsView) sb.Append(" (view)");
            sb.Append(" [C# class: ").Append(table.ClassName).Append("]: ");

            var colDescriptions = table.Columns.OrderBy(c => c.OrdinalPosition).Select(c =>
            {
                var markers = new List<string>();
                if (pkNames.Contains(c.ColumnName)) markers.Add("PK");
                if (fkByColumn.TryGetValue(c.ColumnName, out var refTarget)) markers.Add($"FK->{refTarget}");
                if (c.IsIdentity) markers.Add("identity");

                var markerText = markers.Count > 0 ? $" [{string.Join(",", markers)}]" : "";
                var nullText = c.IsNullable ? "?" : "";
                return $"{c.ColumnName}:{c.SqlDataType}{nullText}{markerText}";
            });

            sb.AppendLine(string.Join(", ", colDescriptions));
        }

        if (procedures is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Stored procedures:");
            foreach (var proc in procedures.OrderBy(p => p.Schema).ThenBy(p => p.Name))
            {
                sb.AppendLine($"- {proc.Schema}.{proc.Name} ({proc.ParameterCount} parameter(s))");
            }
        }

        return sb.ToString();
    }
}
