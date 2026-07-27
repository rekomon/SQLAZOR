namespace SQLAZOR.Models;

public sealed class SqlQueryRunResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public List<string> Columns { get; init; } = [];
    public List<List<string?>> Rows { get; init; } = [];
    public bool Truncated { get; init; }
}
