namespace SQLAZOR.Models;

public enum ImplicitFkConfidence { Medium, High }

/// <summary>
/// A column that *looks* like a foreign key (by naming convention + type match) but has no
/// real FK constraint in the database. The user reviews and opts in per-candidate before it's
/// synthesized into a real ForeignKeyInfo and fed through the normal generation pipeline.
/// </summary>
public sealed class ImplicitForeignKeyCandidate
{
    public required string ParentSchema { get; init; }
    public required string ParentTable { get; init; }
    public required string ParentColumn { get; init; }

    public required string ReferencedSchema { get; init; }
    public required string ReferencedTable { get; init; }
    public required string ReferencedColumn { get; init; }

    public required ImplicitFkConfidence Confidence { get; init; }
    public required string Reason { get; init; }

    public string Key => $"{ParentSchema}.{ParentTable}.{ParentColumn}->{ReferencedSchema}.{ReferencedTable}.{ReferencedColumn}";
}
