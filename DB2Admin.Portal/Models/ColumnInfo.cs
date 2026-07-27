namespace SQLAZOR.Models;

/// <summary>
/// Raw metadata for a single column, as read directly from SQL Server system catalogs.
/// </summary>
public sealed class ColumnInfo
{
    public required string ColumnName { get; init; }
    public required string SqlDataType { get; init; }
    public short MaxLength { get; init; }
    public byte Precision { get; init; }
    public byte Scale { get; init; }
    public bool IsNullable { get; init; }
    public bool IsIdentity { get; init; }
    public bool IsComputed { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsRowGuidCol { get; init; }
    public int OrdinalPosition { get; init; }

    /// <summary>The C# property name this column maps to (PascalCase, mutable so AI naming suggestions can override it).</summary>
    public required string PropertyName { get; set; }

    /// <summary>The resolved C# type, e.g. "int", "string?", "decimal", "Guid".</summary>
    public required string ClrType { get; init; }

    /// <summary>Optional AI-generated one-line summary, rendered as an XML doc comment above the property.</summary>
    public string? Summary { get; set; }
}
