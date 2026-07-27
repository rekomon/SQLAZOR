namespace SQLAZOR.Models;

/// <summary>
/// A table (or view) plus its columns, keys, and indexes. This is the unit the
/// code generator turns into one POCO + one IEntityTypeConfiguration&lt;T&gt;.
/// </summary>
public sealed class TableInfo
{
    public required string Schema { get; init; }
    public required string TableName { get; init; }
    public bool IsView { get; init; }

    /// <summary>Singularized, PascalCase class name derived from TableName (mutable so AI naming suggestions can override it).</summary>
    public required string ClassName { get; set; }

    /// <summary>Optional AI-generated one-line summary, rendered as an XML doc comment above the class.</summary>
    public string? Summary { get; set; }

    public List<ColumnInfo> Columns { get; init; } = [];

    /// <summary>Unique index / candidate key column groups (each inner list is one composite key).</summary>
    public List<List<string>> UniqueIndexes { get; init; } = [];

    public List<string> PrimaryKeyColumns =>
        Columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList();

    public string FullyQualifiedName => $"{Schema}.{TableName}";
}
