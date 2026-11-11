using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class DotnetBuildValidatorTests
{
    private const string Root = @"C:\temp\sqlazor-build-abc";

    [Fact]
    public void Parses_file_diagnostics_and_strips_project_tag()
    {
        var output =
            @"C:\temp\sqlazor-build-abc\App\Entities\Order.cs(12,9): error CS0246: The type or namespace name 'Foo' could not be found [C:\temp\sqlazor-build-abc\App\App.csproj]";

        var report = DotnetBuildValidator.ParseBuildOutput(output, Root);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("App/Entities/Order.cs", diagnostic.FilePath);
        Assert.Equal(12, diagnostic.Line);
        Assert.Equal(9, diagnostic.Column);
        Assert.Equal("CS0246", diagnostic.Id);
        Assert.Equal(ValidationSeverity.Error, diagnostic.Severity);
        Assert.Equal("The type or namespace name 'Foo' could not be found", diagnostic.Message);
        Assert.False(report.IsValid);
    }

    [Fact]
    public void Warnings_are_reported_but_do_not_fail_the_report()
    {
        var output = @"C:\temp\sqlazor-build-abc\App\Program.cs(3,1): warning CS0168: unused variable [C:\temp\sqlazor-build-abc\App\App.csproj]";

        var report = DotnetBuildValidator.ParseBuildOutput(output, Root);

        Assert.True(report.IsValid);
        Assert.Equal(1, report.WarningCount);
    }

    [Fact]
    public void Parses_bare_msbuild_errors()
    {
        var output = "error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.";

        var report = DotnetBuildValidator.ParseBuildOutput(output, Root);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("build", diagnostic.FilePath);
        Assert.Equal("NU1301", diagnostic.Id);
    }

    [Fact]
    public void Duplicate_lines_are_collapsed()
    {
        const string line = @"C:\temp\sqlazor-build-abc\A.cs(1,1): error CS1002: ; expected [C:\temp\sqlazor-build-abc\A.csproj]";

        var report = DotnetBuildValidator.ParseBuildOutput(line + "\n" + line, Root);

        Assert.Single(report.Diagnostics);
    }

    [Fact]
    public void Errors_sort_before_warnings()
    {
        var output =
            @"C:\temp\sqlazor-build-abc\B.cs(1,1): warning CS0168: unused [C:\temp\sqlazor-build-abc\B.csproj]
C:\temp\sqlazor-build-abc\A.cs(1,1): error CS1002: ; expected [C:\temp\sqlazor-build-abc\A.csproj]";

        var report = DotnetBuildValidator.ParseBuildOutput(output, Root);

        Assert.Equal(ValidationSeverity.Error, report.Diagnostics[0].Severity);
    }

    [Fact]
    public void CanCompile_requires_a_project_file()
    {
        var validator = new DotnetBuildValidator();

        Assert.False(validator.CanCompile([new GeneratedFile { RelativePath = "A.cs", Content = "" }]));
        Assert.True(validator.CanCompile([new GeneratedFile { RelativePath = "App.csproj", Content = "<Project />" }]));
    }

    [Fact]
    public async Task Compile_without_project_reports_a_helpful_diagnostic()
    {
        var validator = new DotnetBuildValidator();

        var report = await validator.CompileAsync([new GeneratedFile { RelativePath = "A.cs", Content = "class A { }" }]);

        Assert.False(report.IsValid);
        Assert.Contains("full runnable project", report.Diagnostics.Single().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Real_build_of_a_minimal_generated_project_succeeds()
    {
        var report = await new DotnetBuildValidator().CompileAsync(MinimalProject("public class Good { public int Value { get; set; } }"));

        Assert.True(report.IsValid, string.Join(" | ", report.Diagnostics.Select(d => d.Message)));
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task Real_build_reports_compiler_errors_from_generated_files()
    {
        var report = await new DotnetBuildValidator().CompileAsync(MinimalProject("public class Broken { void M() { int value = ; } }"));

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.FilePath.EndsWith("Class1.cs", StringComparison.OrdinalIgnoreCase));
    }

    private static GeneratedFile[] MinimalProject(string classBody) =>
    [
        new GeneratedFile
        {
            RelativePath = "App/App.csproj",
            Content = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """
        },
        new GeneratedFile { RelativePath = "App/Class1.cs", Content = classBody }
    ];
}
