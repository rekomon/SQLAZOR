namespace SQLAZOR.Models;

public enum ValidationSeverity
{
    Warning,
    Error
}

/// <summary>One problem found while validating generated output, with a 1-based line/column when known.</summary>
public sealed class ValidationDiagnostic
{
    public required string FilePath { get; init; }
    public required string Message { get; init; }
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
    public int Line { get; init; }
    public int Column { get; init; }
    public string? Id { get; init; }
}

/// <summary>
/// Result of parsing every generated file that has a machine-checkable format. Reports
/// errors only; markup, CSS, and JS files are not compiled and are counted as skipped.
/// </summary>
public sealed class GeneratedCodeValidationReport
{
    public List<ValidationDiagnostic> Diagnostics { get; init; } = [];
    public int FilesChecked { get; init; }
    public int FilesSkipped { get; init; }

    public int ErrorCount => Diagnostics.Count(d => d.Severity == ValidationSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == ValidationSeverity.Warning);
    public bool IsValid => ErrorCount == 0;
    public IEnumerable<ValidationDiagnostic> Errors => Diagnostics.Where(d => d.Severity == ValidationSeverity.Error);
}
