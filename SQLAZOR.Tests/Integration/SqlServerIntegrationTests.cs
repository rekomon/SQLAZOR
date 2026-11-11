using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests.Integration;

/// <summary>
/// End-to-end checks against a real SQL Server. Skipped automatically when none is reachable;
/// set SQLAZOR_TEST_SQL to run them explicitly.
/// </summary>
[Collection("SqlServer")]
public sealed class SqlServerIntegrationTests
{
    private readonly SqlTestDatabase _database;
    private readonly SchemaReaderService _reader = new();

    public SqlServerIntegrationTests(SqlTestDatabase database)
    {
        _database = database;
    }

    [SqlServerFact]
    public async Task Reads_tables_columns_relationships_and_views()
    {
        var schema = await _reader.ReadSchemaAsync(_database.ConnectionString, includeViews: true);

        Assert.Contains(schema.Tables, t => t.FullyQualifiedName == "dbo.Customers" && !t.IsView);
        Assert.Contains(schema.Tables, t => t.FullyQualifiedName == "dbo.vwOrderTotals" && t.IsView);

        var orders = schema.Tables.Single(t => t.FullyQualifiedName == "dbo.Orders");
        Assert.Contains(orders.Columns, c => c.ColumnName == "CustomerId" && !c.IsNullable);
        Assert.Contains(orders.Columns, c => c.ColumnName == "ShippedToCustomerId" && c.IsNullable);

        var orderLines = schema.Tables.Single(t => t.FullyQualifiedName == "dbo.OrderLines");
        Assert.Equal(["OrderId", "LineNumber"], orderLines.PrimaryKeyColumns);
        Assert.Empty(orderLines.UniqueIndexes);

        // Only the explicit UNIQUE constraint is reported; the PK's backing index is not.
        var customers = schema.Tables.Single(t => t.FullyQualifiedName == "dbo.Customers");
        Assert.Contains(customers.UniqueIndexes, index => index.Contains("Email"));

        var legacy = schema.Tables.Single(t => t.FullyQualifiedName == "dbo.LegacyNote");
        Assert.Empty(legacy.PrimaryKeyColumns);

        var ordersFks = schema.ForeignKeys
            .Where(fk => fk.ParentTable == "Orders" && fk.ReferencedTable == "Customers")
            .ToList();
        Assert.Equal(2, ordersFks.Count);
    }

    [SqlServerFact]
    public async Task Lists_stored_procedures_and_describes_a_result_set()
    {
        var procedures = await _reader.ReadStoredProceduresAsync(_database.ConnectionString);
        Assert.Contains(procedures, p => p.Name == "GetCustomerOrders");
        Assert.Contains(procedures, p => p.Name == "RebuildSearchIndex");

        var detail = await _reader.ReadProcedureDetailAsync(_database.ConnectionString, "dbo", "GetCustomerOrders");

        Assert.True(detail.CanDescribeResultSet, detail.DescribeError);
        Assert.Single(detail.Parameters);
        Assert.Contains(detail.ResultColumns, c => c.ColumnName == "OrderId");
    }

    [SqlServerFact]
    public async Task Read_only_runner_returns_rows()
    {
        var result = await _reader.ExecuteReadOnlyQueryAsync(_database.ConnectionString, "SELECT 1 AS Answer");

        Assert.True(result.Success, result.Error);
        Assert.Equal(["Answer"], result.Columns);
        Assert.Single(result.Rows);
    }

    [SqlServerFact]
    public async Task Read_only_runner_rejects_write_statements()
    {
        var result = await _reader.ExecuteReadOnlyQueryAsync(
            _database.ConnectionString, "UPDATE dbo.Customers SET Name = 'nope'");

        Assert.False(result.Success);
        Assert.Contains("SELECT", result.Error);
    }

    [SqlServerFact]
    public async Task Read_only_runner_does_not_run_procedures()
    {
        var result = await _reader.ExecuteReadOnlyQueryAsync(
            _database.ConnectionString, "EXEC dbo.GetCustomerOrders @CustomerId = 1");

        Assert.False(result.Success);
    }
}
