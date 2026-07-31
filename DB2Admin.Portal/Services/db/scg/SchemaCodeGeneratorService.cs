using SQLAZOR.Models;
using System.Text;

namespace SQLAZOR.Services
{
    public sealed class SchemaCodeGeneratorService: ISchemaCodeGeneratorService
    {
      


        #region Generate
        public List<GeneratedFile> Generate(
        DatabaseSchema schema,
        IEnumerable<string> selectedTableKeys,
        string rootNamespace,
        string dbContextName)
        {
            var selected = selectedTableKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tables = schema.Tables.Where(t => selected.Contains(t.FullyQualifiedName)).ToList();
            var tableByKey = tables.ToDictionary(t => t.FullyQualifiedName, StringComparer.OrdinalIgnoreCase);

            // Only keep FKs where both sides are among the selected tables.
            var relevantFks = schema.ForeignKeys
                .Where(fk => tableByKey.ContainsKey($"{fk.ParentSchema}.{fk.ParentTable}")
                          && tableByKey.ContainsKey($"{fk.ReferencedSchema}.{fk.ReferencedTable}"))
                .ToList();

            var (childNavNames, parentNavNames) = BuildNavigationNames(relevantFks, tableByKey);

            var files = new List<GeneratedFile>();

            foreach (var table in tables)
            {
                files.Add(GeneratePoco(table, relevantFks, tableByKey, childNavNames, parentNavNames, rootNamespace));
                files.Add(GenerateConfiguration(table, relevantFks, tableByKey, childNavNames, parentNavNames, rootNamespace));
            }

            files.Add(GenerateDbContext(tables, rootNamespace, dbContextName));

            return files;
        }
        #endregion


        #region Build Navigation Names
        private static (Dictionary<ForeignKeyInfo, string> ChildNav, Dictionary<ForeignKeyInfo, string> ParentNav)
        BuildNavigationNames(List<ForeignKeyInfo> fks, Dictionary<string, TableInfo> tableByKey)
        {
            var childNav = new Dictionary<ForeignKeyInfo, string>();
            var parentNav = new Dictionary<ForeignKeyInfo, string>();

            // --- Reference nav (on the table holding the FK column) ---
            var byParentTable = fks.GroupBy(fk => $"{fk.ParentSchema}.{fk.ParentTable}");
            foreach (var group in byParentTable)
            {
                var candidates = new Dictionary<ForeignKeyInfo, string>();
                foreach (var fk in group)
                {
                    var referencedClass = tableByKey[$"{fk.ReferencedSchema}.{fk.ReferencedTable}"].ClassName;
                    var columnBase = StripTrailingId(NamingHelper.ToPascalCase(fk.ParentColumn));

                    var candidate = columnBase.Equals(referencedClass, StringComparison.OrdinalIgnoreCase)
                        ? referencedClass
                        : columnBase;

                    candidates[fk] = candidate;
                }

                // Disambiguate collisions within the same table by falling back to the raw column-derived name.
                var dupGroups = candidates.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                                           .Where(g => g.Count() > 1);
                foreach (var dup in dupGroups)
                {
                    foreach (var kv in dup)
                    {
                        var columnBase = StripTrailingId(NamingHelper.ToPascalCase(kv.Key.ParentColumn));
                        candidates[kv.Key] = columnBase;
                    }
                }

                foreach (var kv in candidates)
                    childNav[kv.Key] = kv.Value;
            }

            // --- Collection nav (on the referenced/principal table) ---
            var byReferencedTable = fks.GroupBy(fk => $"{fk.ReferencedSchema}.{fk.ReferencedTable}");
            foreach (var group in byReferencedTable)
            {
                var candidates = new Dictionary<ForeignKeyInfo, string>();
                foreach (var fk in group)
                {
                    var childClass = tableByKey[$"{fk.ParentSchema}.{fk.ParentTable}"].ClassName;
                    candidates[fk] = NamingHelper.Pluralize(childClass);
                }

                var dupGroups = candidates.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                                           .Where(g => g.Count() > 1);
                foreach (var dup in dupGroups)
                {
                    foreach (var kv in dup)
                    {
                        var childClass = tableByKey[$"{kv.Key.ParentSchema}.{kv.Key.ParentTable}"].ClassName;
                        // Disambiguate using the reference-side name, e.g. "OrdersBilledTo".
                        var suffix = childNav.TryGetValue(kv.Key, out var refName) ? refName : kv.Key.ConstraintName;
                        candidates[kv.Key] = NamingHelper.Pluralize(childClass) + suffix;
                    }
                }

                foreach (var kv in candidates)
                    parentNav[kv.Key] = kv.Value;
            }

            return (childNav, parentNav);
        }
        #endregion

        #region Append Property Summary
        private static void AppendPropertySummary(StringBuilder sb, ColumnInfo col)
        {
            if (string.IsNullOrWhiteSpace(col.Summary))
                return;

            sb.AppendLine("    /// <summary>" + Constant.EscapeXmlDoc(col.Summary) + "</summary>");
        }
        #endregion

       

        #region Strip Trailing Id
        private static string StripTrailingId(string pascalName)
        {
            if (pascalName.EndsWith("Id", StringComparison.Ordinal) && pascalName.Length > 2)
                return pascalName[..^2];
            return pascalName;
        }
        #endregion

        #region POCO generation
        private static GeneratedFile GeneratePoco(
            TableInfo table,
            List<ForeignKeyInfo> fks,
            Dictionary<string, TableInfo> tableByKey,
            Dictionary<ForeignKeyInfo, string> childNav,
            Dictionary<ForeignKeyInfo, string> parentNav,
            string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("namespace " + rootNamespace + ".Entities;");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(table.Summary))
            {
                sb.AppendLine("/// <summary>");
                sb.AppendLine($"/// {Constant.EscapeXmlDoc(table.Summary)}");
                sb.AppendLine("/// </summary>");
            }
            sb.AppendLine($"public class {table.ClassName}");
            sb.AppendLine("{");

            foreach (var col in table.Columns.OrderBy(c => c.OrdinalPosition))
            {
                if (col.IsPrimaryKey)
                {
                    AppendPropertySummary(sb, col);
                    sb.AppendLine($"    public {col.ClrType} {col.PropertyName} {{ get; set; }}" +
                                   (col.ClrType is "string" or "string?" or "byte[]" or "byte[]?" ? " = default!;" : ""));
                }
            }

            // Non-key scalar columns.
            var nonKeyCols = table.Columns.Where(c => !c.IsPrimaryKey).OrderBy(c => c.OrdinalPosition).ToList();
            if (table.Columns.Any(c => c.IsPrimaryKey) && nonKeyCols.Count > 0)
                sb.AppendLine();

            foreach (var col in nonKeyCols)
            {
                var needsInit = col.ClrType is "string" && !col.IsNullable;
                var init = needsInit ? " = string.Empty;" : "";
                AppendPropertySummary(sb, col);
                sb.AppendLine($"    public {col.ClrType} {col.PropertyName} {{ get; set; }}{init}");
            }

            // Reference navigation properties (this table holds the FK).
            var outgoing = fks.Where(fk => fk.ParentSchema == table.Schema && fk.ParentTable == table.TableName).ToList();
            if (outgoing.Count > 0)
            {
                sb.AppendLine();
                foreach (var fk in outgoing)
                {
                    var referencedClass = tableByKey[$"{fk.ReferencedSchema}.{fk.ReferencedTable}"].ClassName;
                    var navName = childNav[fk];
                    var nullableMark = fk.IsParentColumnNullable ? "?" : "";
                    sb.AppendLine($"    public virtual {referencedClass}{nullableMark} {navName}_{referencedClass} {{ get; set; }}" +
                                   (fk.IsParentColumnNullable ? "" : " = default!;"));
                }
            }

            // Collection navigation properties (other tables point at this one).
            var incoming = fks.Where(fk => fk.ReferencedSchema == table.Schema && fk.ReferencedTable == table.TableName).ToList();
            if (incoming.Count > 0)
            {
                sb.AppendLine();
                foreach (var fk in incoming)
                {
                    var childClass = tableByKey[$"{fk.ParentSchema}.{fk.ParentTable}"].ClassName;
                    var navName = parentNav[fk];
                    sb.AppendLine($"    public virtual ICollection<{childClass}> {navName} {{ get; set; }} = new List<{childClass}>();");
                }
            }

            sb.AppendLine("}");

            return new GeneratedFile
            {
                RelativePath = $"Entities/{table.ClassName}.cs",
                Content = sb.ToString()
            };
        }

        #endregion


        // ----------------------------------------------------------------------
        // Fluent API configuration generation
        // ----------------------------------------------------------------------


        #region Fluent API configuration generation
        private static GeneratedFile GenerateConfiguration(
            TableInfo table,
            List<ForeignKeyInfo> fks,
            Dictionary<string, TableInfo> tableByKey,
            Dictionary<ForeignKeyInfo, string> childNav,
            Dictionary<ForeignKeyInfo, string> parentNav,
            string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("using Microsoft.EntityFrameworkCore.Metadata.Builders;");
            sb.AppendLine($"using {rootNamespace}.Entities;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Configurations;");
            sb.AppendLine();
            sb.AppendLine($"public class {table.ClassName}Configuration : IEntityTypeConfiguration<{table.ClassName}>");
            sb.AppendLine("{");
            sb.AppendLine($"    public void Configure(EntityTypeBuilder<{table.ClassName}> builder)");
            sb.AppendLine("    {");

            var schemaArg = table.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase)
                ? $"\"{table.TableName}\""
                : $"\"{table.TableName}\", \"{table.Schema}\"";
            sb.AppendLine($"        builder.ToTable({schemaArg});");
            sb.AppendLine();

            var pkCols = table.Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.OrdinalPosition).ToList();
            if (pkCols.Count == 1)
            {
                sb.AppendLine($"        builder.HasKey(e => e.{pkCols[0].PropertyName});");
            }
            else if (pkCols.Count > 1)
            {
                var keyExpr = string.Join(", ", pkCols.Select(c => $"e.{c.PropertyName}"));
                sb.AppendLine($"        builder.HasKey(e => new {{ {keyExpr} }});");
            }
            sb.AppendLine();

            foreach (var col in table.Columns.OrderBy(c => c.OrdinalPosition))
            {
                sb.AppendLine(BuildPropertyConfig(col));
            }

            if (outgoingFksExist(table, fks))
            {
                sb.AppendLine();
                foreach (var fk in fks.Where(f => f.ParentSchema == table.Schema && f.ParentTable == table.TableName))
                {
                    var referencedClass = tableByKey[$"{fk.ReferencedSchema}.{fk.ReferencedTable}"].ClassName;
                    var navName = childNav[fk];
                    var inverseName = parentNav[fk];
                    var fkColProp = NamingHelper.EscapeIfReserved(NamingHelper.ToPascalCase(fk.ParentColumn));
                    var deleteBehavior = fk.DeleteAction switch
                    {
                        "CASCADE" => "DeleteBehavior.Cascade",
                        "SET_NULL" => "DeleteBehavior.SetNull",
                        "SET_DEFAULT" => "DeleteBehavior.ClientSetNull",
                        _ => "DeleteBehavior.NoAction"
                    };

                    sb.AppendLine($"        builder.HasOne(e => e.{navName}_{referencedClass})");
                    sb.AppendLine($"            .WithMany(e => e.{inverseName})");
                    sb.AppendLine($"            .HasForeignKey(e => e.{fkColProp})");
                    sb.AppendLine($"            .HasConstraintName(\"{fk.ConstraintName}\")");
                    sb.AppendLine($"            .OnDelete({deleteBehavior});");
                    sb.AppendLine();
                }
            }

            // Unique indexes (non-PK).
            foreach (var uniqueGroup in table.UniqueIndexes)
            {
                var props = string.Join(", ", uniqueGroup.Select(c =>
                    $"e.{NamingHelper.EscapeIfReserved(NamingHelper.ToPascalCase(c))}"));
                var expr = uniqueGroup.Count == 1 ? props : $"new {{ {props} }}";
                sb.AppendLine($"        builder.HasIndex(e => {expr}).IsUnique();");
            }

            // Trim a trailing blank line before closing brace for tidiness.
            while (sb.Length > 1 && sb[^1] == '\n' && sb[^2] == '\n')
                sb.Length--;
            sb.AppendLine();
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return new GeneratedFile
            {
                RelativePath = $"Configurations/{table.ClassName}Configuration.cs",
                Content = sb.ToString()
            };

            static bool outgoingFksExist(TableInfo t, List<ForeignKeyInfo> allFks) =>
                allFks.Any(f => f.ParentSchema == t.Schema && f.ParentTable == t.TableName);
        }
        #endregion


        #region Build Property Config
        private static string BuildPropertyConfig(ColumnInfo col)
        {
            var propName = col.PropertyName;
            var parts = new List<string> { $"        builder.Property(e => e.{propName})" };
            parts.Add($"            .HasColumnName(\"{col.ColumnName}\")");
            parts.Add($"            .HasColumnType(\"{col.SqlDataType.ToLowerInvariant()}\")");

            if (col.SqlDataType.ToLowerInvariant() is "nvarchar" or "nchar")
            {
                if (col.MaxLength == -1)
                    parts.Add("            .HasMaxLength(int.MaxValue)");
                else
                    parts.Add($"            .HasMaxLength({col.MaxLength / 2})");
            }
            else if (col.SqlDataType.ToLowerInvariant() is "varchar" or "char")
            {
                if (col.MaxLength == -1)
                    parts.Add("            // varchar(max)");
                else
                    parts.Add($"            .HasMaxLength({col.MaxLength})");
            }
            else if (col.SqlDataType.ToLowerInvariant() is "decimal" or "numeric")
            {
                parts.Add($"            .HasPrecision({col.Precision}, {col.Scale})");
            }

            if (col.IsIdentity)
                parts.Add("            .ValueGeneratedOnAdd()");
            else if (col.IsComputed)
                parts.Add("            .ValueGeneratedOnAddOrUpdate()");

            if (col.IsRowGuidCol)
                parts.Add("            .HasDefaultValueSql(\"(newid())\")");

            if (!col.IsNullable && !col.IsPrimaryKey)
                parts.Add("            .IsRequired()");

            return string.Join("\n", parts) + ";";
        }
        #endregion

        // ----------------------------------------------------------------------
        // DbContext generation
        // ----------------------------------------------------------------------

        #region DbContext generation
        private static GeneratedFile GenerateDbContext(List<TableInfo> tables, string rootNamespace, string dbContextName)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine($"using {rootNamespace}.Entities;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace};");
            sb.AppendLine();
            sb.AppendLine($"public class {dbContextName} : DbContext");
            sb.AppendLine("{");
            sb.AppendLine($"    public {dbContextName}(DbContextOptions<{dbContextName}> options) : base(options)");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();

            foreach (var table in tables.OrderBy(t => t.ClassName))
            {
                var setName = NamingHelper.Pluralize(table.ClassName);
                sb.AppendLine($"    public DbSet<{table.ClassName}> {setName} => Set<{table.ClassName}>();");
            }

            sb.AppendLine();
            sb.AppendLine("    protected override void OnModelCreating(ModelBuilder modelBuilder)");
            sb.AppendLine("    {");
            sb.AppendLine("        base.OnModelCreating(modelBuilder);");
            sb.AppendLine();
            sb.AppendLine("        modelBuilder.ApplyConfigurationsFromAssembly(typeof(" + dbContextName + ").Assembly);");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return new GeneratedFile
            {
                RelativePath = $"{dbContextName}.cs",
                Content = sb.ToString()
            };
        }
        #endregion

    }
}
