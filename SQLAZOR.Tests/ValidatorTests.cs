using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class ValidatorTests
{
    private static readonly GeneratedCodeValidator Validator = new();

    [Fact]
    public void Valid_csharp_has_no_diagnostics()
    {
        var report = Validate(File("Entities/Good.cs", """
            namespace X.Entities;

            public class Good
            {
                public int Id { get; set; }
                public string Name { get; set; } = string.Empty;
            }
            """));

        Assert.True(report.IsValid);
        Assert.Equal(1, report.FilesChecked);
        Assert.Equal(0, report.FilesSkipped);
    }

    [Fact]
    public void Csharp_syntax_error_reports_file_and_line()
    {
        var report = Validate(File("Entities/Broken.cs", """
            namespace X.Entities;

            public class Broken
            {
                public void M() { int value = ; }
            }
            """));

        Assert.False(report.IsValid);
        var error = Assert.Single(report.Errors);
        Assert.Equal("Entities/Broken.cs", error.FilePath);
        Assert.Equal(5, error.Line);
        Assert.False(string.IsNullOrWhiteSpace(error.Id));
    }

    [Fact]
    public void Razor_code_block_error_is_mapped_back_to_razor_line()
    {
        var content = "@page \"/x\"\n\n@code {\n    private int _value = ;\n}\n";

        var report = Validate(File("Components/Pages/X.razor", content));

        Assert.False(report.IsValid);
        var error = Assert.Single(report.Errors);
        Assert.Equal(4, error.Line);
    }

    [Fact]
    public void Unbalanced_razor_code_block_is_reported()
    {
        var content = "@page \"/x\"\n@code {\n    private int _value = 1;\n";

        var report = Validate(File("Components/Pages/X.razor", content));

        Assert.False(report.IsValid);
        var error = Assert.Single(report.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains("Unbalanced braces", error.Message);
    }

    [Fact]
    public void Missing_opening_brace_after_code_directive_is_reported()
    {
        var report = Validate(File("Components/Pages/X.razor", "@code\nprivate int _value = 1;\n"));

        Assert.False(report.IsValid);
        var error = Assert.Single(report.Errors);
        Assert.Equal(1, error.Line);
        Assert.Contains("opening brace", error.Message);
    }

    [Fact]
    public void Braces_and_quotes_inside_strings_do_not_confuse_block_scanning()
    {
        var content = """
            @page "/x"

            @code {
                private string _json = "{\"a\":1}";
                private string _verbatim = @"{ ""nested"": { } }";
                private char _brace = '}';

                private void M() { }
            }
            """;

        var report = Validate(File("Components/Pages/X.razor", content));

        Assert.True(report.IsValid);
    }

    [Fact]
    public void Razor_functions_block_is_also_parsed()
    {
        var content = """
            @page "/x"

            @functions {
                private void Broken() { int value = ; }
            }
            """;

        var report = Validate(File("Components/Pages/X.razor", content));

        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
    }

    [Fact]
    public void Invalid_json_is_reported()
    {
        var report = Validate(File("appsettings.json", "{\n  \"a\": 1,\n  \"b\"\n}\n"));

        Assert.False(report.IsValid);
        var error = Assert.Single(report.Errors);
        Assert.Equal("appsettings.json", error.FilePath);
        Assert.True(error.Line > 0);
    }

    [Fact]
    public void Json_with_comments_is_accepted()
    {
        var report = Validate(File("appsettings.json", """
            {
              // generated projects ship this note
              "ConnectionStrings": { "DefaultConnection": "Server=.;" }
            }
            """));

        Assert.True(report.IsValid);
    }

    [Fact]
    public void Invalid_project_xml_is_reported()
    {
        var report = Validate(File("ShopApp.csproj", "<Project>\n  <PropertyGroup>\n</Project>\n"));

        Assert.False(report.IsValid);
        Assert.Equal("ShopApp.csproj", Assert.Single(report.Errors).FilePath);
    }

    [Fact]
    public void Non_code_files_are_counted_as_skipped()
    {
        var report = Validate(
            File("wwwroot/css/admin.css", "body { margin: 0; }"),
            File("wwwroot/js/charts.js", "window.charts = [];"),
            File("README.md", "# App"));

        Assert.True(report.IsValid);
        Assert.Equal(0, report.FilesChecked);
        Assert.Equal(3, report.FilesSkipped);
    }

    private static GeneratedCodeValidationReport Validate(params GeneratedFile[] files) =>
        Validator.Validate(files);

    private static GeneratedFile File(string path, string content) =>
        new() { RelativePath = path, Content = content };
}
