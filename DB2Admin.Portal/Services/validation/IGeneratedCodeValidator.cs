using SQLAZOR.Models;

namespace SQLAZOR.Services;

public interface IGeneratedCodeValidator
{
    /// <summary>
    /// Parses every generated file that has a checkable format: C# via Roslyn, the C# inside
    /// Razor <c>@code</c>/<c>@functions</c> blocks, JSON for appsettings, and XML for project files.
    /// Nothing is written to disk and nothing is compiled.
    /// </summary>
    GeneratedCodeValidationReport Validate(IEnumerable<GeneratedFile> files);
}
