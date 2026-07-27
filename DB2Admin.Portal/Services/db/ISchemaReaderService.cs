using SQLAZOR.Models;

namespace SQLAZOR.Services;

public interface ISchemaReaderService
{
    /// <summary>Quick connectivity check before attempting a full schema read.</summary>
    Task<(bool Success, string? Error)> TestConnectionAsync(string connectionString, CancellationToken ct = default);

    Task<List<string>> GetDatabasesList(string connectionString, CancellationToken ct = default);

    /// <summary>
    /// Reads all user tables (optionally filtered to specific schemas) plus their columns,
    /// primary keys, unique indexes, and foreign keys.
    /// </summary>
    Task<DatabaseSchema> ReadSchemaAsync(
        string connectionString,
        IEnumerable<string>? schemaFilter = null,
        bool includeViews = false,
        CancellationToken ct = default);

    /// <summary>Lists stored procedures (schema, name, parameter count) — cheap, no result-set describing.</summary>
    Task<List<StoredProcedureSummary>> ReadStoredProceduresAsync(
        string connectionString,
        IEnumerable<string>? schemaFilter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Reads full parameter list and attempts to describe the first result set of one procedure.
    /// Never throws for "can't describe" cases — check <see cref="StoredProcedureDetail.CanDescribeResultSet"/>.
    /// </summary>
    Task<StoredProcedureDetail> ReadProcedureDetailAsync(
        string connectionString,
        string schema,
        string procedureName,
        CancellationToken ct = default);

    /// <summary>
    /// Runs a SELECT/WITH-only query and returns up to <paramref name="maxRows"/> rows.
    /// Rejects anything that isn't a pure read statement (INSERT/UPDATE/DELETE/DROP/ALTER/EXEC/etc.
    /// all fail the check) — this exists to let the chat assistant's generated queries be run
    /// safely, not as a general-purpose SQL runner.
    /// </summary>
    Task<SqlQueryRunResult> ExecuteReadOnlyQueryAsync(
        string connectionString,
        string sql,
        int maxRows = 200,
        CancellationToken ct = default);
}
