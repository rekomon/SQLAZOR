using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class FileEditAssistantTests
{
    [Fact]
    public void BuildPrompt_contains_file_instruction_and_content()
    {
        var prompt = FileEditAssistant.BuildPrompt(
            "Entities/Order.cs", "public class Order { }", "add XML docs", "- dbo.Orders [C# class: Order]");

        Assert.Contains("Entities/Order.cs", prompt);
        Assert.Contains("add XML docs", prompt);
        Assert.Contains("public class Order { }", prompt);
        Assert.Contains("dbo.Orders", prompt);
        Assert.Contains("\"content\"", prompt);
    }

    [Fact]
    public void TryParse_reads_summary_and_content()
    {
        var suggestion = FileEditAssistant.TryParse(
            """{"summary":"Added docs","content":"public class Order { // docs }"}""");

        Assert.NotNull(suggestion);
        Assert.Equal("Added docs", suggestion!.Summary);
        Assert.StartsWith("public class Order", suggestion.Content);
    }

    [Fact]
    public void TryParse_tolerates_markdown_fences()
    {
        var suggestion = FileEditAssistant.TryParse(
            "```json\n{\"summary\":\"x\",\"content\":\"file body\"}\n```");

        Assert.NotNull(suggestion);
        Assert.Equal("file body", suggestion!.Content);
    }

    [Fact]
    public void TryParse_falls_back_when_summary_missing()
    {
        var suggestion = FileEditAssistant.TryParse("""{"content":"file body"}""");

        Assert.NotNull(suggestion);
        Assert.Equal("Proposed update", suggestion!.Summary);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"summary":"no content"}""")]
    [InlineData("""{"summary":"empty","content":"  "}""")]
    public void TryParse_rejects_unusable_replies(string json) =>
        Assert.Null(FileEditAssistant.TryParse(json));
}
