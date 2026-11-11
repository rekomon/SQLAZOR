using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class SchemaGenerationTests
{
    private const string RootNamespace = "Shop.Data";

    [Fact]
    public void Generated_files_match_approved_snapshots()
    {
        var files = Generate();

        SnapshotAssert.MatchFiles("Schema", files);
    }

    [Fact]
    public void Generated_files_have_no_syntax_errors()
    {
        var files = Generate();

        Assert.NotEmpty(files);
        GenerationAssert.HasNoSyntaxErrors(files);
    }

    private static List<GeneratedFile> Generate() =>
        new SchemaCodeGeneratorService().Generate(
            TestSchema.Create(),
            TestSchema.TableKeys,
            RootNamespace,
            "ShopDbContext");
}
