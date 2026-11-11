using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

internal static class GenerationAssert
{
    private static readonly GeneratedCodeValidator Validator = new();

    public static void HasNoSyntaxErrors(IEnumerable<GeneratedFile> files)
    {
        var report = Validator.Validate(files);
        Assert.True(report.IsValid, Describe(report));
    }

    private static string Describe(GeneratedCodeValidationReport report)
    {
        var lines = report.Diagnostics.Select(d =>
            $"  {d.FilePath}:{d.Line}:{d.Column} {d.Id} {d.Message}");

        return $"{report.ErrorCount} syntax error(s) in generated output:{Environment.NewLine}" +
               string.Join(Environment.NewLine, lines);
    }
}
