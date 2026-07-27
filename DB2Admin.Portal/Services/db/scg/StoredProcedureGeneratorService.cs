using SQLAZOR.Models;
using System.Reflection.PortableExecutable;
using System.Text;

namespace SQLAZOR.Services
{
    public sealed class StoredProcedureGeneratorService : IStoredProcedureGeneratorService
    {

        // ----------------------------------------------------------------------
        // Stored procedure generation
        // ----------------------------------------------------------------------

        #region Generate For Procedures
        public List<GeneratedFile> GenerateForProcedures(
       IEnumerable<StoredProcedureDetail> procedures,
       string rootNamespace)
        {
            var files = new List<GeneratedFile>();

            foreach (var proc in procedures)
            {
                var hasOutputParams = proc.Parameters.Any(p => p.IsOutput);

                if (proc.CanDescribeResultSet)
                {
                    files.Add(GenerateProcedureResultPoco(proc, rootNamespace));
                }

                if (hasOutputParams)
                {
                    files.Add(GenerateProcedureOutputPoco(proc, rootNamespace));
                }

                files.Add(GenerateProcedureExecutor(proc, rootNamespace, hasOutputParams));
            }

            return files;
        }
        #endregion

        #region Generate Procedure Result Poco

        private static GeneratedFile GenerateProcedureResultPoco(StoredProcedureDetail proc, string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine($"namespace {rootNamespace}.Entities;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// One row of the first result set returned by [{proc.Schema}].[{proc.Name}].");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public class {proc.ClassBaseName}Result");
            sb.AppendLine("{");

            foreach (var col in proc.ResultColumns.OrderBy(c => c.Ordinal))
            {
                var needsInit = col.ClrType is "string" && !col.IsNullable;
                var init = needsInit ? " = string.Empty;" : "";
                sb.AppendLine($"    public {col.ClrType} {col.PropertyName} {{ get; set; }}{init}");
            }

            sb.AppendLine("}");

            return new GeneratedFile
            {
                RelativePath = $"Entities/{proc.ClassBaseName}Result.cs",
                Content = sb.ToString()
            };
        }
        #endregion

        #region Generate Procedure Output Poco
        private static GeneratedFile GenerateProcedureOutputPoco(StoredProcedureDetail proc, string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine($"namespace {rootNamespace}.Entities;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// Output parameter values from [{proc.Schema}].[{proc.Name}], populated after the reader is closed.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public class {proc.ClassBaseName}Output");
            sb.AppendLine("{");

            foreach (var p in proc.Parameters.Where(p => p.IsOutput).OrderBy(p => p.Ordinal))
            {
                sb.AppendLine($"    public {p.ClrType} {p.PropertyName} {{ get; set; }}");
            }

            sb.AppendLine("}");

            return new GeneratedFile
            {
                RelativePath = $"Entities/{proc.ClassBaseName}Output.cs",
                Content = sb.ToString()
            };
        }
        #endregion

        #region Generate Procedure Executor
        private static GeneratedFile GenerateProcedureExecutor(StoredProcedureDetail proc, string rootNamespace, bool hasOutputParams)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using System.Data;");
            sb.AppendLine("using Microsoft.Data.SqlClient;");
            sb.AppendLine($"using {rootNamespace}.Entities;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Data;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// Hand-mapped caller for [{proc.Schema}].[{proc.Name}]. Uses plain ADO.NET (no EF) so the");
            sb.AppendLine("/// parameter binding and row mapping are explicit and easy to audit or hand-tune.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public static class {proc.ClassBaseName}Executor");
            sb.AppendLine("{");

            var inputParams = proc.Parameters.OrderBy(p => p.Ordinal).ToList();
            var methodParams = string.Join(", ", inputParams.Select(p => $"{p.ClrType} {Constant.LowerFirst(p.PropertyName)} = default"));
            var methodParamsSig = methodParams.Length > 0 ? methodParams + ", " : "";

            // Determine the return type / wrapper.
            string returnType;
            if (proc.CanDescribeResultSet && hasOutputParams)
                returnType = $"{proc.ClassBaseName}ExecutionResult";
            else if (proc.CanDescribeResultSet)
                returnType = $"List<{proc.ClassBaseName}Result>";
            else if (hasOutputParams)
                returnType = $"{proc.ClassBaseName}Output";
            else
                returnType = "int"; // rows affected, since there's nothing else to hand back

            sb.AppendLine($"    public static async Task<{returnType}> ExecuteAsync(");
            sb.AppendLine($"        SqlConnection connection, {methodParamsSig}CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        await using var command = new SqlCommand(\"[{proc.Schema}].[{proc.Name}]\", connection)");
            sb.AppendLine("        {");
            sb.AppendLine("            CommandType = CommandType.StoredProcedure");
            sb.AppendLine("        };");
            sb.AppendLine();

            // Declare a SqlParameter local for every parameter (needed so output values can be read back later).
            var outputLocalNames = new Dictionary<ProcedureParameterInfo, string>();
            foreach (var p in inputParams)
            {
                var localName = "param" + p.PropertyName;
                var sqlDbType = MapSqlTypeToSqlDbType(p.SqlDataType);
                var sizeArg = NeedsSize(p.SqlDataType)
                    ? $", {(p.MaxLength == -1 ? "-1" : p.MaxLength.ToString())}"
                    : "";

                var initLines = new List<string>();

                if (p.IsOutput)
                {
                    initLines.Add("Direction = ParameterDirection.Output");
                    outputLocalNames[p] = localName;
                }
                else
                {
                    var argName = Constant.LowerFirst(p.PropertyName);
                    var isReferenceOrNullable = p.ClrType.EndsWith("?") || p.ClrType is "string" or "byte[]";
                    initLines.Add(isReferenceOrNullable
                        ? $"Value = (object?){argName} ?? DBNull.Value"
                        : $"Value = {argName}");
                }

                if (p.SqlDataType.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
                    p.SqlDataType.Equals("numeric", StringComparison.OrdinalIgnoreCase))
                {
                    initLines.Add($"Precision = {p.Precision}");
                    initLines.Add($"Scale = {p.Scale}");
                }

                sb.AppendLine($"        var {localName} = new SqlParameter(\"{p.ParameterName}\", SqlDbType.{sqlDbType}{sizeArg})");
                sb.AppendLine("        {");
                AppendInitializerLines(sb, "            ", initLines);
                sb.AppendLine("        };");
                sb.AppendLine($"        command.Parameters.Add({localName});");
                sb.AppendLine();
            }

            if (proc.CanDescribeResultSet)
            {
                var resultVar = hasOutputParams ? "rows" : "results";
                sb.AppendLine($"        var {resultVar} = new List<{proc.ClassBaseName}Result>();");
                sb.AppendLine();
                sb.AppendLine("        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))");
                sb.AppendLine("        {");
                sb.AppendLine("            while (await reader.ReadAsync(cancellationToken))");
                sb.AppendLine("            {");
                sb.AppendLine($"                {resultVar}.Add(Map(reader));");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
            }
            else
            {
                sb.AppendLine(hasOutputParams
                    ? "        await command.ExecuteNonQueryAsync(cancellationToken);"
                    : "        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);");
            }

            sb.AppendLine();

            if (hasOutputParams)
            {
                var outputInitLines = outputLocalNames
                    .Select(kv => $"{kv.Key.PropertyName} = {kv.Value}.Value == DBNull.Value ? null : ({kv.Key.ClrType}){kv.Value}.Value")
                    .ToList();

                sb.AppendLine($"        var output = new {proc.ClassBaseName}Output");
                sb.AppendLine("        {");
                AppendInitializerLines(sb, "            ", outputInitLines);
                sb.AppendLine("        };");
                sb.AppendLine();

                sb.AppendLine(proc.CanDescribeResultSet
                    ? $"        return new {proc.ClassBaseName}ExecutionResult {{ Rows = rows, Output = output }};"
                    : "        return output;");
            }
            else if (proc.CanDescribeResultSet)
            {
                sb.AppendLine("        return results;");
            }
            else
            {
                sb.AppendLine("        return rowsAffected;");
            }

            sb.AppendLine("    }");

            if (proc.CanDescribeResultSet)
            {
                var mapInitLines = proc.ResultColumns
                    .OrderBy(c => c.Ordinal)
                    .Select(col => $"{col.PropertyName} = {BuildReaderGetter(col)}")
                    .ToList();

                sb.AppendLine();
                sb.AppendLine($"    private static {proc.ClassBaseName}Result Map(SqlDataReader reader)");
                sb.AppendLine("    {");
                sb.AppendLine($"        return new {proc.ClassBaseName}Result");
                sb.AppendLine("        {");
                AppendInitializerLines(sb, "            ", mapInitLines);
                sb.AppendLine("        };");
                sb.AppendLine("    }");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("    // SQL Server could not describe this procedure's result set" +
                               (proc.DescribeError is not null ? " (" + proc.DescribeError.Replace("\n", " ").Replace("\r", "") + ")." : "."));
                sb.AppendLine("    // If it does return rows, add an ExecuteReaderAsync overload here and map columns by hand.");
            }

            sb.AppendLine("}");

            if (hasOutputParams && proc.CanDescribeResultSet)
            {
                sb.AppendLine();
                sb.AppendLine($"public sealed class {proc.ClassBaseName}ExecutionResult");
                sb.AppendLine("{");
                sb.AppendLine($"    public List<{proc.ClassBaseName}Result> Rows {{ get; init; }} = [];");
                sb.AppendLine($"    public required {proc.ClassBaseName}Output Output {{ get; init; }}");
                sb.AppendLine("}");
            }

            return new GeneratedFile
            {
                RelativePath = $"Data/{proc.ClassBaseName}Executor.cs",
                Content = sb.ToString()
            };
        }
        #endregion

        #region Append Initializer Lines
        private static void AppendInitializerLines(StringBuilder sb, string indent, List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var suffix = i < lines.Count - 1 ? "," : "";
                sb.AppendLine($"{indent}{lines[i]}{suffix}");
            }
        }

        #endregion

        #region Build Reader Getter
        private static string BuildReaderGetter(ProcedureResultColumn col)
        {
            var ordinalExpr = $"reader.GetOrdinal(\"{col.ColumnName}\")";
            var baseType = col.ClrType.TrimEnd('?');

            var getter = baseType switch
            {
                "long" => "GetInt64",
                "bool" => "GetBoolean",
                "string" => "GetString",
                "DateTime" => "GetDateTime",
                "DateTimeOffset" => "GetDateTimeOffset",
                "decimal" => "GetDecimal",
                "double" => "GetDouble",
                "int" => "GetInt32",
                "short" => "GetInt16",
                "byte" => "GetByte",
                "TimeSpan" => "GetTimeSpan",
                "Guid" => "GetGuid",
                "float" => "GetFloat",
                "byte[]" => "GetSqlBinary",
                _ => "GetValue"
            };

            var accessor = getter == "GetSqlBinary"
                ? $"(byte[])reader.GetSqlBinary({ordinalExpr}).Value"
                : getter == "GetValue"
                    ? $"({baseType})reader.GetValue({ordinalExpr})"
                    : $"reader.{getter}({ordinalExpr})";

            if (col.IsNullable)
            {
                return $"reader.IsDBNull({ordinalExpr}) ? null : {accessor}";
            }

            return accessor;
        }
        #endregion

       

        #region Needs Size
        private static bool NeedsSize(string sqlType) => sqlType.ToLowerInvariant() switch
        {
            "varchar" or "nvarchar" or "char" or "nchar" or "varbinary" or "binary" => true,
            _ => false
        };
        #endregion

        #region Map SqlType To Sql DbType
        private static string MapSqlTypeToSqlDbType(string sqlType) => sqlType.ToLowerInvariant() switch
        {
            "bigint" => "BigInt",
            "binary" => "Binary",
            "bit" => "Bit",
            "char" => "Char",
            "date" => "Date",
            "datetime" => "DateTime",
            "datetime2" => "DateTime2",
            "datetimeoffset" => "DateTimeOffset",
            "decimal" => "Decimal",
            "numeric" => "Decimal",
            "float" => "Float",
            "image" => "Image",
            "int" => "Int",
            "money" => "Money",
            "nchar" => "NChar",
            "ntext" => "NText",
            "nvarchar" => "NVarChar",
            "real" => "Real",
            "smalldatetime" => "SmallDateTime",
            "smallint" => "SmallInt",
            "smallmoney" => "SmallMoney",
            "text" => "Text",
            "time" => "Time",
            "timestamp" or "rowversion" => "Timestamp",
            "tinyint" => "TinyInt",
            "uniqueidentifier" => "UniqueIdentifier",
            "varbinary" => "VarBinary",
            "varchar" => "VarChar",
            "xml" => "Xml",
            "sql_variant" => "Variant",
            _ => "NVarChar"
        };
        #endregion

        

    }
}
