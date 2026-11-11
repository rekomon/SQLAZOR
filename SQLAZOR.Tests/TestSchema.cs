using SQLAZOR.Models;
using SQLAZOR.Services;

namespace SQLAZOR.Tests;

/// <summary>
/// An in-memory schema shaped like what <c>SchemaReaderService</c> returns, covering the cases
/// the generators branch on: identity/computed columns, composite PKs, two FKs to the same table,
/// a unique index, a view, a table without a PK, and describable/non-describable procedures.
/// No database is needed to run these tests.
/// </summary>
internal static class TestSchema
{
    public static readonly string[] TableKeys =
    [
        "dbo.Customers",
        "dbo.Orders",
        "dbo.OrderLines",
        "dbo.Products",
        "dbo.LegacyNote",
        "dbo.vwOrderTotals"
    ];

    public static readonly string[] CrudTableKeys =
    [
        "dbo.Customers",
        "dbo.Orders",
        "dbo.OrderLines",
        "dbo.Products",
        "dbo.LegacyNote"
    ];

    public static DatabaseSchema Create()
    {
        var customers = new TableInfo
        {
            Schema = "dbo",
            TableName = "Customers",
            ClassName = "Customer",
            Summary = "People who place orders.",
            Columns = BuildColumns(
                new("CustomerId", "int", PrimaryKey: true, Identity: true),
                new("FirstName", "nvarchar", MaxLength: 200),
                new("LastName", "nvarchar", MaxLength: 200),
                new("Email", "nvarchar", Nullable: true, MaxLength: 512),
                new("CreatedAt", "datetime2"),
                new("RowVersion", "rowversion")),
            UniqueIndexes = [["Email"]]
        };

        var orders = new TableInfo
        {
            Schema = "dbo",
            TableName = "Orders",
            ClassName = "Order",
            Summary = "A sale that references a billing customer and an optional shipping customer.",
            Columns = BuildColumns(
                new("OrderId", "int", PrimaryKey: true, Identity: true),
                new("CustomerId", "int"),
                new("ShippedToCustomerId", "int", Nullable: true),
                new("OrderDate", "datetime2"),
                new("Total", "decimal", Precision: 18, Scale: 2),
                new("Status", "nvarchar", MaxLength: 40),
                new("RowVersion", "rowversion"))
        };

        var orderLines = new TableInfo
        {
            Schema = "dbo",
            TableName = "OrderLines",
            ClassName = "OrderLine",
            Summary = "One product line within an order.",
            Columns = BuildColumns(
                new("OrderId", "int", PrimaryKey: true),
                new("LineNumber", "int", PrimaryKey: true),
                new("ProductId", "int"),
                new("Quantity", "int"),
                new("UnitPrice", "decimal", Precision: 18, Scale: 2)),
            UniqueIndexes = [["OrderId", "ProductId"]]
        };

        var products = new TableInfo
        {
            Schema = "dbo",
            TableName = "Products",
            ClassName = "Product",
            Summary = "Sellable items.",
            Columns = BuildColumns(
                new("ProductId", "int", PrimaryKey: true, Identity: true),
                new("Sku", "nvarchar", MaxLength: 80),
                new("Name", "nvarchar", MaxLength: 400),
                new("Category", "nvarchar", Nullable: true, MaxLength: 160),
                new("Price", "decimal", Precision: 18, Scale: 2),
                new("Stock", "int"),
                new("StockValue", "decimal", Computed: true, Precision: 18, Scale: 2),
                new("AddedOn", "datetimeoffset"),
                new("IsActive", "bit")),
            UniqueIndexes = [["Sku"]]
        };

        var legacyNote = new TableInfo
        {
            Schema = "dbo",
            TableName = "LegacyNote",
            ClassName = "LegacyNote",
            Summary = "Free-form notes imported from the old system. Deliberately has no primary key.",
            Columns = BuildColumns(
                new("LegacyCode", "varchar", MaxLength: 10),
                new("Note", "text", Nullable: true),
                new("EnteredAt", "smalldatetime"))
        };

        var totalsView = new TableInfo
        {
            Schema = "dbo",
            TableName = "vwOrderTotals",
            ClassName = "VwOrderTotal",
            IsView = true,
            Columns = BuildColumns(
                new("OrderId", "int"),
                new("CustomerName", "nvarchar", Nullable: true, MaxLength: 400),
                new("Total", "decimal", Precision: 18, Scale: 2))
        };

        return new DatabaseSchema
        {
            DatabaseName = "ShopDb",
            Tables = [customers, orders, orderLines, products, legacyNote, totalsView],
            ForeignKeys =
            [
                new ForeignKeyInfo
                {
                    ConstraintName = "FK_Orders_Customers_CustomerId",
                    ParentSchema = "dbo", ParentTable = "Orders", ParentColumn = "CustomerId",
                    ReferencedSchema = "dbo", ReferencedTable = "Customers", ReferencedColumn = "CustomerId",
                    IsParentColumnNullable = false,
                    DeleteAction = "CASCADE"
                },
                new ForeignKeyInfo
                {
                    ConstraintName = "FK_Orders_Customers_ShippedToCustomerId",
                    ParentSchema = "dbo", ParentTable = "Orders", ParentColumn = "ShippedToCustomerId",
                    ReferencedSchema = "dbo", ReferencedTable = "Customers", ReferencedColumn = "CustomerId",
                    IsParentColumnNullable = true,
                    DeleteAction = "NO_ACTION"
                },
                new ForeignKeyInfo
                {
                    ConstraintName = "FK_OrderLines_Orders_OrderId",
                    ParentSchema = "dbo", ParentTable = "OrderLines", ParentColumn = "OrderId",
                    ReferencedSchema = "dbo", ReferencedTable = "Orders", ReferencedColumn = "OrderId",
                    IsParentColumnNullable = false,
                    DeleteAction = "CASCADE"
                },
                new ForeignKeyInfo
                {
                    ConstraintName = "FK_OrderLines_Products_ProductId",
                    ParentSchema = "dbo", ParentTable = "OrderLines", ParentColumn = "ProductId",
                    ReferencedSchema = "dbo", ReferencedTable = "Products", ReferencedColumn = "ProductId",
                    IsParentColumnNullable = false,
                    DeleteAction = "NO_ACTION"
                }
            ]
        };
    }

    public static List<StoredProcedureDetail> Procedures() =>
    [
        new StoredProcedureDetail
        {
            Schema = "dbo",
            Name = "GetCustomerOrders",
            ClassBaseName = "GetCustomerOrder",
            CanDescribeResultSet = true,
            Parameters =
            [
                new ProcedureParameterInfo
                {
                    ParameterName = "@CustomerId", SqlDataType = "int", Ordinal = 1,
                    PropertyName = "CustomerId", ClrType = "int"
                },
                new ProcedureParameterInfo
                {
                    ParameterName = "@FromDate", SqlDataType = "datetime2", Ordinal = 2,
                    IsOutput = false, HasDefaultValue = true,
                    PropertyName = "FromDate", ClrType = "DateTime?"
                }
            ],
            ResultColumns =
            [
                new ProcedureResultColumn
                {
                    ColumnName = "OrderId", SqlDataType = "int", Ordinal = 1,
                    PropertyName = "OrderId", ClrType = "int"
                },
                new ProcedureResultColumn
                {
                    ColumnName = "OrderDate", SqlDataType = "datetime2", Ordinal = 2,
                    PropertyName = "OrderDate", ClrType = "DateTime"
                },
                new ProcedureResultColumn
                {
                    ColumnName = "Total", SqlDataType = "decimal", Ordinal = 3,
                    PropertyName = "Total", ClrType = "decimal"
                }
            ]
        },
        new StoredProcedureDetail
        {
            Schema = "dbo",
            Name = "RebuildSearchIndex",
            ClassBaseName = "RebuildSearchIndex",
            CanDescribeResultSet = false,
            DescribeError = "Invalid object name '#indexWork'.",
            Parameters =
            [
                new ProcedureParameterInfo
                {
                    ParameterName = "@Force", SqlDataType = "bit", Ordinal = 1,
                    PropertyName = "Force", ClrType = "bool"
                },
                new ProcedureParameterInfo
                {
                    ParameterName = "@RebuiltCount", SqlDataType = "int", Ordinal = 2,
                    IsOutput = true,
                    PropertyName = "RebuiltCount", ClrType = "int"
                }
            ]
        }
    ];

    public static List<DashboardInsightCandidate> Insights() =>
    [
        new DashboardInsightCandidate
        {
            Title = "OrdersByStatus",
            ChartType = InsightChartType.Bar,
            Sql = "SELECT [Status] AS [Label], COUNT(*) AS [Value] FROM [Orders] GROUP BY [Status]",
            Description = "Order volume per status.",
            IsValid = true,
            PreviewLabels = ["New", "Shipped"],
            PreviewValues = [12, 7]
        },
        new DashboardInsightCandidate
        {
            Title = "RevenueByMonth",
            ChartType = InsightChartType.Line,
            Sql = "SELECT FORMAT([OrderDate], 'yyyy-MM') AS [Label], SUM([Total]) AS [Value] FROM [Orders] GROUP BY FORMAT([OrderDate], 'yyyy-MM')",
            IsValid = true,
            PreviewLabels = ["2026-01"],
            PreviewValues = [100d]
        }
    ];

    private static List<ColumnInfo> BuildColumns(params ColumnSpec[] specs)
    {
        var columns = new List<ColumnInfo>(specs.Length);
        for (var i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            columns.Add(new ColumnInfo
            {
                ColumnName = spec.Name,
                SqlDataType = spec.SqlType,
                IsNullable = spec.Nullable,
                IsPrimaryKey = spec.PrimaryKey,
                IsIdentity = spec.Identity,
                IsComputed = spec.Computed,
                MaxLength = spec.MaxLength,
                Precision = spec.Precision,
                Scale = spec.Scale,
                OrdinalPosition = i + 1,
                PropertyName = NamingHelper.ToPascalCase(spec.Name),
                ClrType = NamingHelper.MapSqlTypeToClr(spec.SqlType, spec.Nullable, spec.Precision, spec.Scale)
            });
        }

        return columns;
    }

    private readonly record struct ColumnSpec(
        string Name,
        string SqlType,
        bool Nullable = false,
        bool PrimaryKey = false,
        bool Identity = false,
        bool Computed = false,
        short MaxLength = 0,
        byte Precision = 0,
        byte Scale = 0);
}
