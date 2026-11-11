using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class LookupAssistantTests
{
    private static readonly DatabaseSchema Schema = TestSchema.Create();
    private static readonly string[] Scope = ["dbo.Orders", "dbo.Customers", "dbo.Products"];

    [Fact]
    public void Parses_static_lookup_and_canonicalizes_table_names()
    {
        const string json = """
            {"lookups":[
              {"table":"Orders","column":"Status","referencedTable":null,"valueColumn":null,
               "labelColumns":[],"options":["New","Shipped"],"reason":"Order workflow."}
            ]}
            """;

        var suggestions = LookupAssistant.Parse(json, Schema, Scope);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("dbo.Orders", suggestion.TableKey);
        Assert.Equal("Status", suggestion.ColumnName);
        Assert.Equal(LookupSuggestionKind.Static, suggestion.Kind);
        Assert.Equal(["New", "Shipped"], suggestion.Options);
    }

    [Fact]
    public void Parses_reference_lookup_with_label_columns()
    {
        const string json = """
            {"lookups":[
              {"table":"dbo.Orders","column":"Status","referencedTable":"Customers",
               "valueColumn":null,"labelColumns":["FirstName","LastName"],"options":[],"reason":"Copied name."}
            ]}
            """;

        var suggestion = Assert.Single(LookupAssistant.Parse(json, Schema, Scope));

        Assert.Equal("dbo.Customers", suggestion.ReferencedTableKey);
        Assert.Null(suggestion.ValueColumn);
        Assert.Equal(["FirstName", "LastName"], suggestion.LabelColumns);
    }

    [Fact]
    public void Parses_json_inside_markdown_fence()
    {
        const string json = """
            ```json
            {"lookups":[{"table":"dbo.Products","column":"Category","options":["A","B"],"reason":"x"}]}
            ```
            """;

        var suggestion = Assert.Single(LookupAssistant.Parse(json, Schema, Scope));

        Assert.Equal("dbo.Products", suggestion.TableKey);
        Assert.Equal("Category", suggestion.ColumnName);
    }

    [Fact]
    public void Drops_unknown_tables_and_columns()
    {
        const string json = """
            {"lookups":[
              {"table":"dbo.Unknown","column":"Status","options":["A","B"]},
              {"table":"dbo.Orders","column":"NotAColumn","options":["A","B"]},
              {"table":"dbo.Orders","column":"Status","referencedTable":"dbo.Products","labelColumns":["Nope"]}
            ]}
            """;

        var suggestion = Assert.Single(LookupAssistant.Parse(json, Schema, Scope));

        Assert.Equal("Status", suggestion.ColumnName);
        Assert.Empty(suggestion.LabelColumns);
    }

    [Fact]
    public void Malformed_json_returns_no_suggestions()
    {
        Assert.Empty(LookupAssistant.Parse("not json", Schema, Scope));
    }

    [Fact]
    public void Prompt_contains_selected_tables_and_foreign_keys_only()
    {
        var prompt = LookupAssistant.BuildPrompt(Schema, Scope);

        Assert.Contains("dbo.Orders", prompt);
        Assert.Contains("dbo.Customers", prompt);
        Assert.DoesNotContain("dbo.LegacyNote", prompt);
        Assert.Contains("dbo.Orders.CustomerId -> dbo.Customers.CustomerId", prompt);
    }
}
