namespace SQLAZOR.Models;

/// <summary>Optional extras layered on top of entity/config generation.</summary>
public sealed class GenerationOptions
{
    public bool GenerateCrudServices { get; set; }
    public bool GenerateApiEndpoints { get; set; }
    public bool GenerateHttpClientServices { get; set; }
    public bool GenerateBlazorPages { get; set; }
    public PageStyle PageStyle { get; set; } = PageStyle.Plain;
    public bool GenerateProjectScaffold { get; set; }
    public bool GenerateDocumentation { get; set; }
    public bool UseCleanArchitecture { get; set; }
}
