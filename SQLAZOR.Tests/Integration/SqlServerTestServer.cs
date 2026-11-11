using Microsoft.Data.SqlClient;
using Xunit;

namespace SQLAZOR.Tests.Integration;

/// <summary>
/// Finds a SQL Server the integration tests may use. Set SQLAZOR_TEST_SQL to a connection string
/// (the tests create and drop a temporary database, so it needs CREATE DATABASE permission);
/// otherwise LocalDB and then the default instance are probed briefly.
/// </summary>
public static class SqlServerTestServer
{
    private static readonly Lazy<(string? ConnectionString, string Reason)> Probe = new(ProbeCore, isThreadSafe: true);

    public static string? MasterConnectionString => Probe.Value.ConnectionString;
    public static bool IsAvailable => MasterConnectionString is not null;
    public static string UnavailableReason => Probe.Value.Reason;

    private static (string?, string) ProbeCore()
    {
        var configured = Environment.GetEnvironmentVariable("SQLAZOR_TEST_SQL");
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
                candidates.Add(builder.ConnectionString);
            }
            catch (ArgumentException ex)
            {
                return (null, $"SQLAZOR_TEST_SQL is not a valid connection string: {ex.Message}");
            }
        }
        else
        {
            candidates.Add("Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=2");
            candidates.Add("Server=.;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=2");
        }

        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            try
            {
                using var connection = new SqlConnection(candidate);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.ExecuteScalar();
                return (candidate, string.Empty);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        return (null, "No reachable SQL Server. Set SQLAZOR_TEST_SQL or start LocalDB. " + string.Join(" | ", errors));
    }
}

/// <summary>A [Fact] that is skipped (with a reason) when no SQL Server is available.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!SqlServerTestServer.IsAvailable)
            Skip = SqlServerTestServer.UnavailableReason;
    }
}
