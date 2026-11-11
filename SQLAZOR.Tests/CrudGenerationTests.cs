using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class CrudGenerationTests
{
    private const string RootNamespace = "Shop.Data";

    [Fact]
    public void Plain_full_stack_matches_approved_snapshots()
    {
        var files = Generate(PageStyle.Plain, endpoints: true, httpServices: true, pages: true);

        SnapshotAssert.MatchFiles("Crud/PlainFull", files);
    }

    [Fact]
    public void MudBlazor_pages_match_approved_snapshots()
    {
        var files = Generate(PageStyle.MudBlazor, endpoints: false, httpServices: false, pages: true);

        SnapshotAssert.MatchFiles("Crud/MudBlazorPages", files);
    }

    [Fact]
    public void Tabler_pages_match_approved_snapshots()
    {
        var files = Generate(PageStyle.Tabler, endpoints: false, httpServices: false, pages: true);

        SnapshotAssert.MatchFiles("Crud/TablerPages", files);
    }

    [Theory]
    [InlineData(PageStyle.Plain, true, true, true)]
    [InlineData(PageStyle.MudBlazor, true, true, true)]
    [InlineData(PageStyle.Tabler, true, true, true)]
    public void Generated_files_have_no_syntax_errors(PageStyle pageStyle, bool endpoints, bool httpServices, bool pages)
    {
        var files = Generate(pageStyle, endpoints, httpServices, pages);

        Assert.NotEmpty(files);
        GenerationAssert.HasNoSyntaxErrors(files);
    }

    [Fact]
    public void MudBlazor_pages_render_foreign_keys_as_searchable_autocompletes()
    {
        var files = Generate(PageStyle.MudBlazor, endpoints: false, httpServices: false, pages: true);

        var create = files.Single(f => f.RelativePath.EndsWith("Order/OrderCreate.razor", StringComparison.Ordinal));
        Assert.Contains("@inject ICustomerService CustomerLookupService", create.Content);
        Assert.Contains(
            "<MudAutocomplete T=\"int\" @bind-Value=\"_model.CustomerId\" SearchFunc=\"SearchCustomerIdAsync\" ToStringFunc=\"CustomerIdLabel\"",
            create.Content);
        Assert.Contains("CustomerLookupService.SearchAsync(value, 50, token)", create.Content);
        Assert.DoesNotContain("CustomerLookupService.GetAllAsync()", create.Content);
        Assert.Contains("_customerIdLabels", create.Content);
        Assert.Contains("_customerIdLabels[row.CustomerId] = $\"{row.FirstName} {row.LastName}\";", create.Content);

        var edit = files.Single(f => f.RelativePath.EndsWith("Order/OrderEdit.razor", StringComparison.Ordinal));
        Assert.Contains(
            "<MudAutocomplete T=\"int?\" @bind-Value=\"_model.ShippedToCustomerId\" SearchFunc=\"SearchShippedToCustomerIdAsync\"",
            edit.Content);
        Assert.Contains("CustomerLookupService.GetByIdAsync(currentShippedToCustomerId)", edit.Content);
        Assert.Contains("_shippedToCustomerIdLabels[currentShippedToCustomerId] =", edit.Content);
    }

    [Fact]
    public void Plain_pages_render_foreign_keys_as_selects()
    {
        var files = Generate(PageStyle.Plain, endpoints: false, httpServices: false, pages: true);

        var create = files.Single(f => f.RelativePath.EndsWith("Order/OrderCreate.razor", StringComparison.Ordinal));
        Assert.Contains("<select class=\"form-select\"", create.Content);
        Assert.Contains("<option value=\"@option.CustomerId\">@($\"{option.FirstName} {option.LastName}\")</option>", create.Content);
    }

    [Fact]
    public void Ai_static_lookup_renders_fixed_choices()
    {
        var schema = TestSchema.Create();
        var plan = LookupPlanner.Build(schema, TestSchema.CrudTableKeys,
        [
            new LookupSuggestion
            {
                TableKey = "dbo.Orders",
                ColumnName = "Status",
                Options = ["New", "Shipped"]
            }
        ]);

        var files = new CrudGeneratorService().GenerateCrudServices(
            schema, TestSchema.CrudTableKeys, RootNamespace,
            generateApiEndpoints: false, generateHttpClientServices: false, generateBlazorPages: true,
            PageStyle.MudBlazor, layers: null, lookupPlan: plan);

        var create = files.Single(f => f.RelativePath.EndsWith("Order/OrderCreate.razor", StringComparison.Ordinal));
        Assert.Contains("<MudSelect T=\"string\" @bind-Value=\"_model.Status\" Label=\"Status\"", create.Content);
        Assert.Contains("<MudSelectItem T=\"string\" Value=\"@(\"New\")\">New</MudSelectItem>", create.Content);
        Assert.Contains("<MudSelectItem T=\"string\" Value=\"@(\"Shipped\")\">Shipped</MudSelectItem>", create.Content);
    }

    [Fact]
    public void Ai_text_reference_stores_the_display_label()
    {
        var schema = TestSchema.Create();
        var plan = LookupPlanner.Build(schema, TestSchema.CrudTableKeys,
        [
            new LookupSuggestion
            {
                TableKey = "dbo.Orders",
                ColumnName = "Status",
                ReferencedTableKey = "dbo.Customers",
                LabelColumns = ["FirstName", "LastName"]
            }
        ]);

        var files = new CrudGeneratorService().GenerateCrudServices(
            schema, TestSchema.CrudTableKeys, RootNamespace,
            generateApiEndpoints: false, generateHttpClientServices: false, generateBlazorPages: true,
            PageStyle.MudBlazor, layers: null, lookupPlan: plan);

        var create = files.Single(f => f.RelativePath.EndsWith("Order/OrderCreate.razor", StringComparison.Ordinal));
        Assert.Contains("<MudAutocomplete T=\"string\" @bind-Value=\"_model.Status\" SearchFunc=\"SearchStatusAsync\"", create.Content);
        Assert.Contains("Select(row => (string)($\"{row.FirstName} {row.LastName}\"))", create.Content);
        Assert.DoesNotContain("SearchStatusAsync\" ToStringFunc", create.Content);
    }

    [Fact]
    public void Key_only_reference_tables_use_a_readable_label()
    {
        var parent = new TableInfo
        {
            Schema = "dbo",
            TableName = "Codes",
            ClassName = "Code",
            Columns =
            [
                new ColumnInfo { ColumnName = "CodeId", SqlDataType = "int", ClrType = "int", PropertyName = "CodeId", IsPrimaryKey = true, OrdinalPosition = 1 },
                new ColumnInfo { ColumnName = "Ordinal", SqlDataType = "int", ClrType = "int", PropertyName = "Ordinal", OrdinalPosition = 2 }
            ]
        };
        var child = new TableInfo
        {
            Schema = "dbo",
            TableName = "Widgets",
            ClassName = "Widget",
            Columns =
            [
                new ColumnInfo { ColumnName = "WidgetId", SqlDataType = "int", ClrType = "int", PropertyName = "WidgetId", IsPrimaryKey = true, OrdinalPosition = 1 },
                new ColumnInfo { ColumnName = "CodeId", SqlDataType = "int", ClrType = "int", PropertyName = "CodeId", OrdinalPosition = 2 }
            ]
        };
        var schema = new DatabaseSchema
        {
            DatabaseName = "ShopDb",
            Tables = [parent, child],
            ForeignKeys =
            [
                new ForeignKeyInfo
                {
                    ConstraintName = "FK_Widgets_Codes",
                    ParentSchema = "dbo", ParentTable = "Widgets", ParentColumn = "CodeId",
                    ReferencedSchema = "dbo", ReferencedTable = "Codes", ReferencedColumn = "CodeId",
                    IsParentColumnNullable = false, DeleteAction = "NO_ACTION"
                }
            ]
        };

        var files = new CrudGeneratorService().GenerateCrudServices(
            schema, ["dbo.Widgets", "dbo.Codes"], RootNamespace,
            generateApiEndpoints: false, generateHttpClientServices: false, generateBlazorPages: true,
            PageStyle.MudBlazor);

        var create = files.Single(f => f.RelativePath.EndsWith("Widget/WidgetCreate.razor", StringComparison.Ordinal));
        Assert.Contains("$\"Code #{row.CodeId}\"", create.Content);
    }

    [Fact]
    public void Services_expose_a_cached_search_method()
    {
        var files = Generate(PageStyle.Plain, endpoints: true, httpServices: true, pages: true);

        var iface = files.Single(f => f.RelativePath.EndsWith("Services/IOrderService.cs", StringComparison.Ordinal));
        Assert.Contains("Task<ResponseResult<List<OrderDto>>> SearchAsync(string? term, int take = 50, CancellationToken cancellationToken = default);", iface.Content);

        var service = files.Single(f => f.RelativePath.EndsWith("Services/OrderService.cs", StringComparison.Ordinal));
        Assert.Contains("TOP (@take)", service.Content);
        Assert.Contains("LIKE @pattern ESCAPE '~'", service.Content);
        Assert.Contains("SearchCacheTtl = TimeSpan.FromSeconds(60)", service.Content);
        Assert.Contains("EscapeLike(query)", service.Content);
        Assert.Contains(".Replace(\"~\", \"~~\")", service.Content);

        var controller = files.Single(f => f.RelativePath.EndsWith("Controllers/OrdersController.cs", StringComparison.Ordinal));
        Assert.Contains("[HttpGet(\"search\")]", controller.Content);

        var http = files.Single(f => f.RelativePath.EndsWith("Services/OrderHttpService.cs", StringComparison.Ordinal));
        Assert.Contains("/search?term=", http.Content);
    }

    [Fact]
    public void Search_matches_only_text_columns_and_orders_by_a_label_column()
    {
        var files = Generate(PageStyle.Plain, endpoints: false, httpServices: false, pages: true);

        var service = files.Single(f => f.RelativePath.EndsWith("Services/CustomerService.cs", StringComparison.Ordinal));
        // Customer's searchable text columns: FirstName, LastName, Email - ordered by the label columns.
        Assert.Contains("[FirstName] LIKE @pattern ESCAPE '~' OR [LastName] LIKE @pattern ESCAPE '~'", service.Content);
        Assert.Contains("ORDER BY [FirstName]", service.Content);
    }

    private static List<GeneratedFile> Generate(PageStyle pageStyle, bool endpoints, bool httpServices, bool pages) =>
        new CrudGeneratorService().GenerateCrudServices(
            TestSchema.Create(),
            TestSchema.CrudTableKeys,
            RootNamespace,
            endpoints,
            httpServices,
            pages,
            pageStyle);
}
