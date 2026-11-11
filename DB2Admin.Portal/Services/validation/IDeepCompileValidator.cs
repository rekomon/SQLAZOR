using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Optional, slower than the syntax validator: materializes the generated project in a temp
/// directory and runs <c>dotnet build</c>, so missing usings, unknown types and bad DI wiring
/// are caught. Requires the generated set to contain a .csproj.
/// </summary>
public interface IDeepCompileValidator
{
    bool CanCompile(IReadOnlyList<GeneratedFile> files);
    Task<GeneratedCodeValidationReport> CompileAsync(IReadOnlyList<GeneratedFile> files, CancellationToken ct = default);
}
