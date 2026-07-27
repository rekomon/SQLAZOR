namespace SQLAZOR.Models;

public sealed class DatabaseSchema
{
    public required string DatabaseName { get; init; }
    public List<TableInfo> Tables { get; init; } = [];
    public List<ForeignKeyInfo> ForeignKeys { get; init; } = [];
}

/// <summary>
/// A single generated source file, ready to preview or write to disk.
/// </summary>
public sealed class GeneratedFile
{
    public required string RelativePath { get; init; }
    public required string Content { get; init; }
}
