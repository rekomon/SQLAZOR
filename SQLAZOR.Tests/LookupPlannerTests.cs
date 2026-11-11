using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class LookupPlannerTests
{
    [Fact]
    public void Foreign_keys_become_reference_lookups_with_picked_labels()
    {
        var plan = LookupPlanner.Build(TestSchema.Create(), TestSchema.CrudTableKeys);
        var orders = plan.ByTable["dbo.Orders"];

        var customer = Assert.Single(orders.Columns, c => c.Column.ColumnName == "CustomerId");
        Assert.Equal(LookupValueSource.ReferencedColumn, customer.ValueSource);
        Assert.Equal("Customers", customer.ReferencedTable!.TableName);
        Assert.Equal("CustomerId", customer.ValueColumn!.ColumnName);
        Assert.Equal(["FirstName", "LastName"], customer.LabelColumns.Select(c => c.ColumnName));

        var shippedTo = Assert.Single(orders.Columns, c => c.Column.ColumnName == "ShippedToCustomerId");
        Assert.Equal("Customers", shippedTo.ReferencedTable!.TableName);

        var lines = plan.ByTable["dbo.OrderLines"];
        Assert.Contains(lines.Columns, c => c.Column.ColumnName == "OrderId");
        Assert.Contains(lines.Columns, c => c.Column.ColumnName == "ProductId");
        Assert.Equal(["Name"], lines.ForColumn("ProductId")!.LabelColumns.Select(c => c.ColumnName));
    }

    [Fact]
    public void Ai_text_mapping_stores_display_text_and_uses_supplied_labels()
    {
        var suggestion = new LookupSuggestion
        {
            TableKey = "dbo.Orders",
            ColumnName = "Status",
            ReferencedTableKey = "dbo.Customers",
            LabelColumns = ["FirstName", "LastName"],
            Reason = "Status is copied from the customer record."
        };

        var plan = LookupPlanner.Build(TestSchema.Create(), TestSchema.CrudTableKeys, [suggestion]);
        var lookup = plan.ByTable["dbo.Orders"].ForColumn("Status")!;

        Assert.Equal(LookupValueSource.DisplayText, lookup.ValueSource);
        Assert.Null(lookup.ValueColumn);
        Assert.Equal(["FirstName", "LastName"], lookup.LabelColumns.Select(c => c.ColumnName));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Ai_static_choices_fill_gaps_for_categorical_columns()
    {
        var suggestion = new LookupSuggestion
        {
            TableKey = "dbo.Orders",
            ColumnName = "Status",
            Options = ["New", "Shipped", "Closed"],
            Reason = "Standard order workflow."
        };

        var plan = LookupPlanner.Build(TestSchema.Create(), TestSchema.CrudTableKeys, [suggestion]);
        var lookup = plan.ByTable["dbo.Orders"].ForColumn("Status")!;

        Assert.Equal(LookupValueSource.StaticList, lookup.ValueSource);
        Assert.Equal(["New", "Shipped", "Closed"], lookup.StaticOptions);
    }

    [Fact]
    public void Ai_reference_to_unselected_table_is_rejected_with_warning()
    {
        var suggestion = new LookupSuggestion
        {
            TableKey = "dbo.Orders",
            ColumnName = "Status",
            ReferencedTableKey = "dbo.MissingTable",
            LabelColumns = ["Name"]
        };

        var plan = LookupPlanner.Build(TestSchema.Create(), TestSchema.CrudTableKeys, [suggestion]);

        Assert.DoesNotContain(
            "Status",
            plan.ByTable["dbo.Orders"].Columns.Select(c => c.Column.ColumnName));
        Assert.Contains(plan.Warnings, w => w.Contains("MissingTable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ai_suggestion_for_a_real_foreign_key_is_ignored()
    {
        var suggestion = new LookupSuggestion
        {
            TableKey = "dbo.Orders",
            ColumnName = "CustomerId",
            ReferencedTableKey = "dbo.Products",
            ValueColumn = "ProductId"
        };

        var plan = LookupPlanner.Build(TestSchema.Create(), TestSchema.CrudTableKeys, [suggestion]);
        var lookup = plan.ByTable["dbo.Orders"].ForColumn("CustomerId")!;

        Assert.Equal("Customers", lookup.ReferencedTable!.TableName);
        Assert.Contains(plan.Warnings, w => w.Contains("already has a foreign-key dropdown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Foreign_key_to_an_unselected_table_is_reported()
    {
        var plan = LookupPlanner.Build(TestSchema.Create(), ["dbo.Orders"]);

        Assert.DoesNotContain(
            "CustomerId",
            plan.ByTable["dbo.Orders"].Columns.Select(c => c.Column.ColumnName));
        Assert.Contains(plan.Warnings, w =>
            w.Contains("Orders.CustomerId", StringComparison.Ordinal) &&
            w.Contains("Customers", StringComparison.Ordinal) &&
            w.Contains("isn't selected", StringComparison.Ordinal));
    }

    [Fact]
    public void Static_choices_need_at_least_two_values()
    {
        var suggestion = new LookupSuggestion
        {
            TableKey = "dbo.Orders",
            ColumnName = "Status",
            Options = ["Only"]
        };

        var plan = LookupPlanner.Build(TestSchema.Create(), TestSchema.CrudTableKeys, [suggestion]);

        Assert.DoesNotContain("Status", plan.ByTable["dbo.Orders"].Columns.Select(c => c.Column.ColumnName));
        Assert.NotEmpty(plan.Warnings);
    }
}
