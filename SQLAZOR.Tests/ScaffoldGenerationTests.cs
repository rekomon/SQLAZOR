using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class ScaffoldGenerationTests
{
    private const string RootNamespace = "Shop.Data";
    private const string ApplicationName = "ShopApp";
    private const string ConnectionString =
        "Server=.;Database=ShopDb;User Id=sa;Password=NotARealPassword1!;TrustServerCertificate=True;";

    [Fact]
    public void Single_project_matches_approved_snapshots()
    {
        var files = GenerateSingleProject();

        SnapshotAssert.MatchFiles("Scaffold/SingleProject", files);
    }

    [Fact]
    public void Clean_architecture_matches_approved_snapshots()
    {
        var files = GenerateCleanArchitecture();

        SnapshotAssert.MatchFiles("Scaffold/CleanArchitecture", files);
    }

    [Fact]
    public void Single_project_has_no_syntax_errors()
    {
        GenerationAssert.HasNoSyntaxErrors(GenerateSingleProject());
    }

    [Fact]
    public void Clean_architecture_has_no_syntax_errors()
    {
        GenerationAssert.HasNoSyntaxErrors(GenerateCleanArchitecture());
    }

    private static List<GeneratedFile> GenerateSingleProject()
    {
        var schema = TestSchema.Create();
        var files = new List<GeneratedFile>();

        files.AddRange(new SchemaCodeGeneratorService().Generate(
            schema, TestSchema.CrudTableKeys, RootNamespace, "ShopDbContext"));
        files.AddRange(new CrudGeneratorService().GenerateCrudServices(
            schema, TestSchema.CrudTableKeys, RootNamespace, true, true, true, PageStyle.Plain));
        files.AddRange(new ProjectScaffoldGeneratorService().GenerateProjectScaffold(
            schema, TestSchema.CrudTableKeys, RootNamespace, ApplicationName, "ShopDbContext",
            ConnectionString, true, true, true, PageStyle.Plain, TestSchema.Insights()));

        return files;
    }

    private static List<GeneratedFile> GenerateCleanArchitecture()
    {
        var schema = TestSchema.Create();
        var layers = ProjectLayers.Create(true, ApplicationName, RootNamespace);
        var files = new List<GeneratedFile>();

        files.AddRange(new SchemaCodeGeneratorService().Generate(
            schema, TestSchema.CrudTableKeys, RootNamespace, "ShopDbContext", layers));
        files.AddRange(new CrudGeneratorService().GenerateCrudServices(
            schema, TestSchema.CrudTableKeys, RootNamespace, true, true, true, PageStyle.Plain, layers));
        files.AddRange(new ProjectScaffoldGeneratorService().GenerateProjectScaffold(
            schema, TestSchema.CrudTableKeys, RootNamespace, ApplicationName, "ShopDbContext",
            ConnectionString, true, true, true, PageStyle.Plain, TestSchema.Insights(),
            useCleanArchitecture: true));

        return files;
    }
}
