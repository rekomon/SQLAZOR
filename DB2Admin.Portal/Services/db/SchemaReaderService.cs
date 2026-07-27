using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SQLAZOR.Models;
using SQLAZOR.Services;


namespace SQLAZOR.Services;

public sealed class SchemaReaderService : ISchemaReaderService
{
    public async Task<(bool Success, string? Error)> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
    public async Task<List<string>> GetDatabasesList(string connectionString, CancellationToken ct = default) {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        const string sql = "SELECT name FROM sys.databases WHERE state = 0 AND  name NOT IN ('master', 'tempdb', 'model', 'msdb');";
        await using var cmd = new SqlCommand(sql, conn);

        var databases = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }


    public async Task<DatabaseSchema> ReadSchemaAsync(
        string connectionString,
        IEnumerable<string>? schemaFilter = null,
        bool includeViews = false,
        CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var databaseName = conn.Database;
        var schemaSet = schemaFilter?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tables = await ReadTablesAsync(conn, schemaSet, includeViews, ct);
        var columnsByTable = await ReadColumnsAsync(conn, schemaSet, includeViews, ct);
        var primaryKeys = await ReadPrimaryKeysAsync(conn, schemaSet, ct);
        var uniqueIndexes = await ReadUniqueIndexesAsync(conn, schemaSet, ct);
        var foreignKeys = await ReadForeignKeysAsync(conn, schemaSet, ct);

        foreach (var table in tables)
        {
            var key = table.FullyQualifiedName;

            if (columnsByTable.TryGetValue(key, out var cols))
            {
                var pkSet = primaryKeys.TryGetValue(key, out var pks) ? pks : new HashSet<string>();

                foreach (var col in cols)
                {
                    table.Columns.Add(col);
                }

                // Mark PK flags now that we know them (ColumnInfo is immutable via 'required', so rebuild).
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var c = table.Columns[i];
                    if (pkSet.Contains(c.ColumnName))
                    {
                        table.Columns[i] = CloneWithPk(c, true);
                    }
                }
            }

            if (uniqueIndexes.TryGetValue(key, out var idx))
            {
                table.UniqueIndexes.AddRange(idx);
            }
        }

        return new DatabaseSchema
        {
            DatabaseName = databaseName,
            Tables = tables,
            ForeignKeys = foreignKeys
        };
    }

    private static ColumnInfo CloneWithPk(ColumnInfo c, bool isPk) => new()
    {
        ColumnName = c.ColumnName,
        SqlDataType = c.SqlDataType,
        MaxLength = c.MaxLength,
        Precision = c.Precision,
        Scale = c.Scale,
        IsNullable = c.IsNullable,
        IsIdentity = c.IsIdentity,
        IsComputed = c.IsComputed,
        IsPrimaryKey = isPk,
        IsRowGuidCol = c.IsRowGuidCol,
        OrdinalPosition = c.OrdinalPosition,
        PropertyName = c.PropertyName,
        ClrType = c.ClrType
    };

    private static async Task<List<TableInfo>> ReadTablesAsync(
        SqlConnection conn, HashSet<string>? schemaFilter, bool includeViews, CancellationToken ct)
    {
        const string sql = @"
SELECT s.name AS SchemaName, t.name AS TableName, 0 AS IsView
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.is_ms_shipped = 0
UNION ALL
SELECT s.name AS SchemaName, v.name AS TableName, 1 AS IsView
FROM sys.views v
JOIN sys.schemas s ON v.schema_id = s.schema_id
WHERE v.is_ms_shipped = 0 AND @IncludeViews = 1
ORDER BY SchemaName, TableName;";

        var result = new List<TableInfo>();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@IncludeViews", includeViews ? 1 : 0);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            var tableName = reader.GetString(1);
            var isView = reader.GetInt32(2) == 1;

            if (schemaFilter is not null && schemaFilter.Count > 0 && !schemaFilter.Contains(schema))
                continue;

            var className = NamingHelper.Singularize(NamingHelper.ToPascalCase(tableName));

            result.Add(new TableInfo
            {
                Schema = schema,
                TableName = tableName,
                IsView = isView,
                ClassName = className
            });
        }

        return result;
    }

    private static async Task<Dictionary<string, List<ColumnInfo>>> ReadColumnsAsync(
        SqlConnection conn, HashSet<string>? schemaFilter, bool includeViews, CancellationToken ct)
    {
        const string sql = @"
SELECT
    s.name AS SchemaName,
    o.name AS TableName,
    c.name AS ColumnName,
    ty.name AS SqlDataType,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable,
    c.is_identity,
    c.is_computed,
    c.is_rowguidcol,
    c.column_id AS OrdinalPosition
FROM sys.columns c
JOIN sys.objects o ON c.object_id = o.object_id
JOIN sys.schemas s ON o.schema_id = s.schema_id
JOIN sys.types ty ON c.user_type_id = ty.user_type_id
WHERE o.is_ms_shipped = 0
  AND o.type IN ('U', 'V')
  AND (@IncludeViews = 1 OR o.type = 'U')
ORDER BY s.name, o.name, c.column_id;";

        var result = new Dictionary<string, List<ColumnInfo>>();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@IncludeViews", includeViews ? 1 : 0);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            var tableName = reader.GetString(1);

            if (schemaFilter is not null && schemaFilter.Count > 0 && !schemaFilter.Contains(schema))
                continue;

            var columnName = reader.GetString(2);
            var sqlType = reader.GetString(3);
            var maxLength = reader.GetInt16(4);
            var precision = reader.GetByte(5);
            var scale = reader.GetByte(6);
            var isNullable = reader.GetBoolean(7);
            var isIdentity = reader.GetBoolean(8);
            var isComputed = reader.GetBoolean(9);
            var isRowGuid = reader.GetBoolean(10);
            var ordinal = reader.GetInt32(11);

            var propertyName = NamingHelper.EscapeIfReserved(NamingHelper.ToPascalCase(columnName));
            var clrType = NamingHelper.MapSqlTypeToClr(sqlType, isNullable, precision, scale);

            var col = new ColumnInfo
            {
                ColumnName = columnName,
                SqlDataType = sqlType,
                MaxLength = maxLength,
                Precision = precision,
                Scale = scale,
                IsNullable = isNullable,
                IsIdentity = isIdentity,
                IsComputed = isComputed,
                IsPrimaryKey = false,
                IsRowGuidCol = isRowGuid,
                OrdinalPosition = ordinal,
                PropertyName = propertyName,
                ClrType = clrType
            };

            var key = $"{schema}.{tableName}";
            if (!result.TryGetValue(key, out var list))
            {
                list = [];
                result[key] = list;
            }
            list.Add(col);
        }

        return result;
    }

    private static async Task<Dictionary<string, HashSet<string>>> ReadPrimaryKeysAsync(
        SqlConnection conn, HashSet<string>? schemaFilter, CancellationToken ct)
    {
        const string sql = @"
SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.tables t ON i.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE i.is_primary_key = 1;";

        var result = new Dictionary<string, HashSet<string>>();

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            if (schemaFilter is not null && schemaFilter.Count > 0 && !schemaFilter.Contains(schema))
                continue;

            var key = $"{schema}.{reader.GetString(1)}";
            var col = reader.GetString(2);

            if (!result.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[key] = set;
            }
            set.Add(col);
        }

        return result;
    }

    private static async Task<Dictionary<string, List<List<string>>>> ReadUniqueIndexesAsync(
        SqlConnection conn, HashSet<string>? schemaFilter, CancellationToken ct)
    {
        const string sql = @"
SELECT s.name AS SchemaName, t.name AS TableName, i.name AS IndexName, c.name AS ColumnName, ic.key_ordinal
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.tables t ON i.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE i.is_unique = 1 AND i.is_primary_key = 0 AND i.is_disabled = 0
ORDER BY s.name, t.name, i.name, ic.key_ordinal;";

        var byIndex = new Dictionary<string, List<(string Table, string Schema, string Column)>>();

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            if (schemaFilter is not null && schemaFilter.Count > 0 && !schemaFilter.Contains(schema))
                continue;

            var table = reader.GetString(1);
            var indexName = reader.GetString(2);
            var column = reader.GetString(3);

            var groupKey = $"{schema}.{table}.{indexName}";
            if (!byIndex.TryGetValue(groupKey, out var list))
            {
                list = [];
                byIndex[groupKey] = list;
            }
            list.Add((table, schema, column));
        }

        var result = new Dictionary<string, List<List<string>>>();
        foreach (var group in byIndex.Values)
        {
            var tableKey = $"{group[0].Schema}.{group[0].Table}";
            if (!result.TryGetValue(tableKey, out var indexList))
            {
                indexList = [];
                result[tableKey] = indexList;
            }
            indexList.Add(group.Select(g => g.Column).ToList());
        }

        return result;
    }

    private static async Task<List<ForeignKeyInfo>> ReadForeignKeysAsync(
        SqlConnection conn, HashSet<string>? schemaFilter, CancellationToken ct)
    {
        const string sql = @"
SELECT
    fk.name AS ConstraintName,
    ps.name AS ParentSchema,
    tp.name AS ParentTable,
    cp.name AS ParentColumn,
    cp.is_nullable AS ParentColumnNullable,
    rs.name AS ReferencedSchema,
    tr.name AS ReferencedTable,
    cr.name AS ReferencedColumn,
    fk.delete_referential_action_desc AS DeleteAction
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
JOIN sys.schemas ps ON tp.schema_id = ps.schema_id
JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
JOIN sys.schemas rs ON tr.schema_id = rs.schema_id
JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
ORDER BY fk.name;";

        var result = new List<ForeignKeyInfo>();

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var parentSchema = reader.GetString(1);
            var referencedSchema = reader.GetString(5);

            if (schemaFilter is not null && schemaFilter.Count > 0 &&
                (!schemaFilter.Contains(parentSchema) || !schemaFilter.Contains(referencedSchema)))
                continue;

            result.Add(new ForeignKeyInfo
            {
                ConstraintName = reader.GetString(0),
                ParentSchema = parentSchema,
                ParentTable = reader.GetString(2),
                ParentColumn = reader.GetString(3),
                IsParentColumnNullable = reader.GetBoolean(4),
                ReferencedSchema = referencedSchema,
                ReferencedTable = reader.GetString(6),
                ReferencedColumn = reader.GetString(7),
                DeleteAction = reader.GetString(8)
            });
        }

        return result;
    }

    // ----------------------------------------------------------------------
    // Stored procedures
    // ----------------------------------------------------------------------

    public async Task<List<StoredProcedureSummary>> ReadStoredProceduresAsync(
        string connectionString,
        IEnumerable<string>? schemaFilter = null,
        CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var schemaSet = schemaFilter?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        const string sql = @"
SELECT s.name AS SchemaName, p.name AS ProcName,
       (SELECT COUNT(*) FROM sys.parameters pr WHERE pr.object_id = p.object_id) AS ParamCount
FROM sys.procedures p
JOIN sys.schemas s ON p.schema_id = s.schema_id
WHERE p.is_ms_shipped = 0
ORDER BY s.name, p.name;";

        var result = new List<StoredProcedureSummary>();

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            if (schemaSet is not null && schemaSet.Count > 0 && !schemaSet.Contains(schema))
                continue;

            var name = reader.GetString(1);
            var paramCount = reader.GetInt32(2);

            result.Add(new StoredProcedureSummary
            {
                Schema = schema,
                Name = name,
                ClassBaseName = NamingHelper.Singularize(NamingHelper.ToPascalCase(name)),
                ParameterCount = paramCount
            });
        }

        return result;
    }

    public async Task<StoredProcedureDetail> ReadProcedureDetailAsync(
        string connectionString,
        string schema,
        string procedureName,
        CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var parameters = await ReadProcedureParametersAsync(conn, schema, procedureName, ct);
        var classBase = NamingHelper.Singularize(NamingHelper.ToPascalCase(procedureName));

        var (columns, canDescribe, error) = await DescribeResultSetAsync(conn, schema, procedureName, parameters, ct);

        return new StoredProcedureDetail
        {
            Schema = schema,
            Name = procedureName,
            ClassBaseName = classBase,
            Parameters = parameters,
            ResultColumns = columns,
            CanDescribeResultSet = canDescribe,
            DescribeError = error
        };
    }

    private static async Task<List<ProcedureParameterInfo>> ReadProcedureParametersAsync(
        SqlConnection conn, string schema, string procedureName, CancellationToken ct)
    {
        const string sql = @"
SELECT pr.name AS ParameterName, ty.name AS SqlDataType, pr.max_length, pr.precision, pr.scale,
       pr.is_output, pr.has_default_value, pr.parameter_id
FROM sys.parameters pr
JOIN sys.procedures p ON pr.object_id = p.object_id
JOIN sys.schemas s ON p.schema_id = s.schema_id
JOIN sys.types ty ON pr.user_type_id = ty.user_type_id
WHERE s.name = @Schema AND p.name = @ProcName AND pr.parameter_id > 0
ORDER BY pr.parameter_id;";

        var result = new List<ProcedureParameterInfo>();

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@ProcName", procedureName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var paramName = reader.GetString(0); // e.g. "@CustomerId"
            var sqlType = reader.GetString(1);
            var maxLength = reader.GetInt16(2);
            var precision = reader.GetByte(3);
            var scale = reader.GetByte(4);
            var isOutput = reader.GetBoolean(5);
            var hasDefault = reader.GetBoolean(6);
            var ordinal = reader.GetInt32(7);

            var bareName = paramName.TrimStart('@');
            var propertyName = NamingHelper.EscapeIfReserved(NamingHelper.ToPascalCase(bareName));
            // Parameters are always treated as nullable in the generated C# signature so callers
            // can omit optional ones without worrying about default(T) surprises on value types.
            var clrType = NamingHelper.MapSqlTypeToClr(sqlType, isNullable: true);

            result.Add(new ProcedureParameterInfo
            {
                ParameterName = paramName,
                SqlDataType = sqlType,
                MaxLength = maxLength,
                Precision = precision,
                Scale = scale,
                IsOutput = isOutput,
                HasDefaultValue = hasDefault,
                Ordinal = ordinal,
                PropertyName = propertyName,
                ClrType = clrType
            });
        }

        return result;
    }

    /// <summary>
    /// Uses sys.dm_exec_describe_first_result_set to infer the shape of a procedure's first
    /// result set without actually executing its logic. Every input parameter is passed as
    /// NULL purely so the statement is syntactically complete; describe never runs the proc body.
    /// Some procedures (heavy dynamic SQL, dependence on temp tables from outside the batch,
    /// multiple differently-shaped result sets chosen at runtime) simply can't be described —
    /// that's reported back rather than thrown.
    /// </summary>
    private static async Task<(List<ProcedureResultColumn> Columns, bool Success, string? Error)> DescribeResultSetAsync(
        SqlConnection conn, string schema, string procedureName, List<ProcedureParameterInfo> parameters, CancellationToken ct)
    {
        var execArgs = string.Join(", ", parameters.Select(p => $"{p.ParameterName} = NULL"));
        var execStatement = $"EXEC [{schema}].[{procedureName}]" + (execArgs.Length > 0 ? " " + execArgs : "");

        const string describeSql = @"
SELECT column_ordinal, name, is_nullable, system_type_name
FROM sys.dm_exec_describe_first_result_set(@Stmt, NULL, 0)
ORDER BY column_ordinal;";

        var columns = new List<ProcedureResultColumn>();

        try
        {
            await using var cmd = new SqlCommand(describeSql, conn);
            cmd.Parameters.AddWithValue("@Stmt", execStatement);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var ordinal = reader.GetInt32(0);
                var colName = reader.IsDBNull(1) ? $"Column{ordinal}" : reader.GetString(1);
                var isNullable = !reader.IsDBNull(2) && reader.GetBoolean(2);
                var systemTypeName = reader.IsDBNull(3) ? "sql_variant" : reader.GetString(3);

                // system_type_name comes back like "varchar(50)" or "decimal(10,2)" - keep the base type only.
                var baseType = systemTypeName.Split('(')[0].Trim();

                var propertyName = NamingHelper.EscapeIfReserved(NamingHelper.ToPascalCase(colName));
                var clrType = NamingHelper.MapSqlTypeToClr(baseType, isNullable);

                columns.Add(new ProcedureResultColumn
                {
                    ColumnName = colName,
                    SqlDataType = baseType,
                    IsNullable = isNullable,
                    Ordinal = ordinal,
                    PropertyName = propertyName,
                    ClrType = clrType
                });
            }

            if (columns.Count == 0)
            {
                return (columns, false,
                    "SQL Server could not describe a result set for this procedure (it may return no columns, " +
                    "rely on dynamic SQL, or depend on a temp table created outside this statement).");
            }

            return (columns, true, null);
        }
        catch (Exception ex)
        {
            return (columns, false, ex.Message);
        }
    }

    // ----------------------------------------------------------------------
    // Ad-hoc read-only query execution (for the AI chat assistant)
    // ----------------------------------------------------------------------

    private static readonly Regex WriteKeywordPattern = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|EXEC|EXECUTE|MERGE|CREATE|GRANT|REVOKE|DENY|sp_executesql|xp_cmdshell)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<SqlQueryRunResult> ExecuteReadOnlyQueryAsync(
        string connectionString,
        string sql,
        int maxRows = 200,
        CancellationToken ct = default)
    {
        var (isReadOnly, reason) = ValidateReadOnly(sql);
        if (!isReadOnly)
        {
            return new SqlQueryRunResult
            {
                Success = false,
                Error = $"Only SELECT / WITH (CTE) queries can be run from here for safety. {reason} " +
                         "Copy it and run it manually (e.g. in SSMS) if that's intended."
            };
        }

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<List<string?>>();
            var truncated = false;

            while (await reader.ReadAsync(ct))
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new List<string?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row.Add(reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString());
                }
                rows.Add(row);
            }

            return new SqlQueryRunResult { Success = true, Columns = columns, Rows = rows, Truncated = truncated };
        }
        catch (Exception ex)
        {
            return new SqlQueryRunResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Blanks out SQL line comments (--...), block comments (/*...*/), and the *contents* of
    /// single-quoted string literals (keeping the quotes) before any keyword/shape checks run.
    /// Without this, a comment like "-- see create script" or a filter like WHERE Status =
    /// 'Deleted' would false-positive as a write statement even though the query itself is a
    /// plain SELECT — both are common in AI-generated SQL and were the actual cause of false
    /// rejections here.
    /// </summary>
    private static string StripSqlNoiseForAnalysis(string sql)
    {
        var noComments = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        noComments = Regex.Replace(noComments, @"--[^\r\n]*", " ");
        // Collapse '...'-delimited string contents (handling '' as an escaped quote) to avoid
        // scanning inside literal text, while keeping the statement's overall shape intact.
        return Regex.Replace(noComments, @"'(?:[^']|'')*'", "''");
    }

    /// <summary>
    /// Every semicolon-separated statement must start with SELECT or WITH once comments/leading
    /// whitespace are stripped, and none may contain a data-modifying keyword outside of a string
    /// literal or comment. Deliberately conservative — false positives (rejecting a legitimate
    /// read query) are still preferable to false negatives — but the reason is now specific so a
    /// wrongly-rejected query can actually be diagnosed instead of just failing silently the same way.
    /// </summary>
    private static (bool IsReadOnly, string? Reason) ValidateReadOnly(string sql)
    {
        var cleaned = StripSqlNoiseForAnalysis(sql);
        var statements = cleaned.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (statements.Length == 0)
            return (false, "The query looks empty.");

        foreach (var statement in statements)
        {
            var trimmed = statement.TrimStart();
            var startsReadOnly = trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                                  || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

            if (!startsReadOnly)
            {
                var preview = trimmed.Length > 50 ? trimmed[..50] + "…" : trimmed;
                return (false, $"A statement doesn't start with SELECT or WITH: \"{preview}\"");
            }

            var match = WriteKeywordPattern.Match(statement);
            if (match.Success)
            {
                return (false, $"Found the keyword '{match.Value.ToUpperInvariant()}' outside a string literal/comment.");
            }
        }

        return (true, null);
    }
}

