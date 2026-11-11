using Microsoft.Data.SqlClient;
using Xunit;

namespace SQLAZOR.Tests.Integration;

/// <summary>
/// Creates one throwaway database for the integration collection and drops it afterwards.
/// Every object is created with real SQL, so the schema reader is exercised against the server's
/// own catalog views rather than an in-memory fixture.
/// </summary>
public sealed class SqlTestDatabase : IAsyncLifetime
{
    private readonly string? _masterConnectionString = SqlServerTestServer.MasterConnectionString;

    public string DatabaseName { get; } = "SQLAZOR_Test_" + Guid.NewGuid().ToString("N")[..10];
    public string ConnectionString => WithCatalog(DatabaseName);

    public async Task InitializeAsync()
    {
        if (_masterConnectionString is null)
            return;

        await ExecuteAsync(_masterConnectionString, $"CREATE DATABASE [{DatabaseName}];");

        foreach (var batch in SchemaBatches())
        {
            await ExecuteAsync(ConnectionString, batch);
        }
    }

    public async Task DisposeAsync()
    {
        if (_masterConnectionString is null)
            return;

        try
        {
            await ExecuteAsync(_masterConnectionString,
                $"""
                 IF DB_ID(N'{DatabaseName}') IS NOT NULL
                 BEGIN
                     ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                     DROP DATABASE [{DatabaseName}];
                 END
                 """);
        }
        catch (SqlException)
        {
            // Leaving a test database behind is not worth failing the run for.
        }
    }

    private string WithCatalog(string catalog) =>
        new SqlConnectionStringBuilder(_masterConnectionString!) { InitialCatalog = catalog }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    private static IEnumerable<string> SchemaBatches()
    {
        yield return """
            CREATE TABLE dbo.Customers
            (
                CustomerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL,
                Email NVARCHAR(200) NOT NULL,
                RowVersion ROWVERSION NOT NULL,
                CONSTRAINT UQ_Customers_Email UNIQUE (Email)
            );
            """;

        yield return """
            CREATE TABLE dbo.Orders
            (
                OrderId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
                CustomerId INT NOT NULL,
                ShippedToCustomerId INT NULL,
                OrderedAt DATETIME2 NOT NULL CONSTRAINT DF_Orders_OrderedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId),
                CONSTRAINT FK_Orders_ShippedTo FOREIGN KEY (ShippedToCustomerId) REFERENCES dbo.Customers(CustomerId)
            );
            """;

        yield return """
            CREATE TABLE dbo.OrderLines
            (
                OrderId INT NOT NULL,
                LineNumber INT NOT NULL,
                Description NVARCHAR(200) NOT NULL,
                Quantity INT NOT NULL,
                UnitPrice DECIMAL(18,2) NOT NULL,
                CONSTRAINT PK_OrderLines PRIMARY KEY (OrderId, LineNumber),
                CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(OrderId)
            );
            """;

        yield return """
            CREATE TABLE dbo.LegacyNote
            (
                NoteText NVARCHAR(400) NULL
            );
            """;

        yield return """
            CREATE VIEW dbo.vwOrderTotals
            AS
            SELECT o.OrderId, o.CustomerId, SUM(l.Quantity * l.UnitPrice) AS Total
            FROM dbo.Orders o
            JOIN dbo.OrderLines l ON l.OrderId = o.OrderId
            GROUP BY o.OrderId, o.CustomerId;
            """;

        yield return """
            CREATE PROCEDURE dbo.GetCustomerOrders
                @CustomerId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT o.OrderId, o.CustomerId, o.OrderedAt
                FROM dbo.Orders o
                WHERE o.CustomerId = @CustomerId
                ORDER BY o.OrderedAt DESC;
            END
            """;

        yield return """
            CREATE PROCEDURE dbo.RebuildSearchIndex
                @Rows INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET @Rows = 0;
                EXEC('SELECT 1');
            END
            """;
    }
}

[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlTestDatabase>
{
}
