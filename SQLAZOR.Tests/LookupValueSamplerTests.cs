using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class LookupValueSamplerTests
{
    [Theory]
    [InlineData("Status", true)]
    [InlineData("OrderStatus", true)]
    [InlineData("status", true)]
    [InlineData("Order_Status", true)]
    [InlineData("Priority", true)]
    [InlineData("FirstName", false)]
    [InlineData("Email", false)]
    [InlineData("Notes", false)]
    public void Categorical_names_are_recognised(string name, bool expected)
    {
        var column = Column(name, "nvarchar");

        Assert.Equal(expected, LookupValueSampler.IsCategorical(column));
    }

    [Fact]
    public void Non_text_and_generated_columns_are_never_categorical()
    {
        Assert.False(LookupValueSampler.IsCategorical(Column("Status", "int")));
        Assert.False(LookupValueSampler.IsCategorical(Column("Status", "nvarchar", identity: true)));
        Assert.False(LookupValueSampler.IsCategorical(Column("Status", "nvarchar", computed: true)));
    }

    [Fact]
    public void Probe_is_bounded_and_brackets_identifiers()
    {
        var table = new TableInfo
        {
            Schema = "dbo",
            TableName = "Order]Lines",
            ClassName = "OrderLine",
            Columns = [Column("Status]Code", "nvarchar")]
        };

        var sql = LookupValueSampler.BuildSql(table, table.Columns[0]);

        Assert.Contains("TOP (11)", sql);
        Assert.Contains("[dbo].[Order]]Lines]", sql);
        Assert.Contains("[Status]]Code]", sql);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merge_lets_scanned_values_replace_ai_guesses_for_the_same_column()
    {
        var ai = new LookupSuggestion
        {
            TableKey = "dbo.Orders", ColumnName = "Status",
            Options = ["New", "Shipped"], Reason = "AI guess"
        };
        var other = new LookupSuggestion
        {
            TableKey = "dbo.Orders", ColumnName = "CustomerName",
            ReferencedTableKey = "dbo.Customers", LabelColumns = ["FirstName", "LastName"]
        };
        var scanned = new LookupSuggestion
        {
            TableKey = "dbo.Orders", ColumnName = "status",
            Options = ["Open", "Closed"], Reason = "Scanned"
        };

        var merged = LookupSuggestionSet.Merge([ai, other], [scanned]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("Scanned", merged[0].Reason);
        Assert.Equal(["Open", "Closed"], merged[0].Options);
        Assert.Equal("CustomerName", merged[1].ColumnName);
    }

    private static ColumnInfo Column(
        string name, string sqlType, bool identity = false, bool computed = false) => new()
    {
        ColumnName = name,
        SqlDataType = sqlType,
        ClrType = sqlType.Equals("int", StringComparison.OrdinalIgnoreCase) ? "int" : "string",
        PropertyName = name,
        IsIdentity = identity,
        IsComputed = computed,
        MaxLength = sqlType.Equals("int", StringComparison.OrdinalIgnoreCase) ? (short)0 : (short)100
    };
}
