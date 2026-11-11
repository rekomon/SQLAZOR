using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class StoredProcedureGenerationTests
{
    private const string RootNamespace = "Shop.Data";

    [Fact]
    public void Generated_files_match_approved_snapshots()
    {
        var files = Generate();

        SnapshotAssert.MatchFiles("Procedures", files);
    }

    [Fact]
    public void Generated_files_have_no_syntax_errors()
    {
        var files = Generate();

        Assert.NotEmpty(files);
        GenerationAssert.HasNoSyntaxErrors(files);
    }

    private static List<SQLAZOR.Models.GeneratedFile> Generate() =>
        new StoredProcedureGeneratorService().GenerateForProcedures(
            TestSchema.Procedures(),
            RootNamespace);
}
