namespace SQLAZOR.Models;

/// <summary>
/// A foreign key constraint, always expressed as many-to-one from Parent -> Referenced.
/// (Parent = the table holding the FK column; Referenced = the table the FK points to.)
/// </summary>
public sealed class ForeignKeyInfo
{
    public required string ConstraintName { get; init; }

    public required string ParentSchema { get; init; }
    public required string ParentTable { get; init; }
    public required string ParentColumn { get; init; }

    public required string ReferencedSchema { get; init; }
    public required string ReferencedTable { get; init; }
    public required string ReferencedColumn { get; init; }

    public bool IsParentColumnNullable { get; init; }

    /// <summary>Cascade behavior on delete, e.g. "CASCADE", "NO_ACTION", "SET_NULL".</summary>
    public string DeleteAction { get; init; } = "NO_ACTION";
}
