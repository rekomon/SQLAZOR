namespace SQLAZOR.Models;

/// <summary>Lightweight listing entry for a stored procedure (no result-set detail yet).</summary>
public sealed class StoredProcedureSummary
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public required string ClassBaseName { get; init; }
    public int ParameterCount { get; init; }

    public string FullyQualifiedName => $"{Schema}.{Name}";
}

public sealed class ProcedureParameterInfo
{
    public required string ParameterName { get; init; }   // includes leading '@'
    public required string SqlDataType { get; init; }
    public short MaxLength { get; init; }
    public byte Precision { get; init; }
    public byte Scale { get; init; }
    public bool IsOutput { get; init; }
    public bool HasDefaultValue { get; init; }
    public int Ordinal { get; init; }

    public required string PropertyName { get; init; }     // PascalCase, no '@'
    public required string ClrType { get; init; }
}

public sealed class ProcedureResultColumn
{
    public required string ColumnName { get; init; }
    public required string SqlDataType { get; init; }
    public bool IsNullable { get; init; }
    public int Ordinal { get; init; }

    public required string PropertyName { get; init; }
    public required string ClrType { get; init; }
}

/// <summary>
/// Full detail for one stored procedure: its parameters, and — if SQL Server was able to
/// describe it — the shape of its first result set. Some procedures (heavy dynamic SQL,
/// temp-table-dependent logic, etc.) cannot be described; that's recorded rather than thrown.
/// </summary>
public sealed class StoredProcedureDetail
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public required string ClassBaseName { get; init; }

    public List<ProcedureParameterInfo> Parameters { get; init; } = [];
    public List<ProcedureResultColumn> ResultColumns { get; init; } = [];

    public bool CanDescribeResultSet { get; init; }
    public string? DescribeError { get; init; }

    public string FullyQualifiedName => $"{Schema}.{Name}";
}
