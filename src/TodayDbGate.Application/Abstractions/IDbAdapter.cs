using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Abstractions;

/// <summary>Talks to one kind of target database.</summary>
public interface IDbAdapter
{
    DatabaseType DatabaseType { get; }
    int DefaultPort { get; }

    string BuildConnectionString(ConnectionDetails details);
    ConnectionDetails ParseConnectionString(string connectionString);

    /// <exception cref="Exceptions.DatabaseUnavailableException">Connection could not be opened.</exception>
    Task TestConnectionAsync(string connectionString, CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="sql"/> inside a transaction. With <see cref="AccessLevel.ReadOnly"/> the transaction
    /// is put in the database's read-only mode where supported, and always rolled back.
    /// </summary>
    Task<QueryResult> ExecuteQueryAsync(
        string connectionString, string sql, AccessLevel accessLevel, QueryLimits limits, CancellationToken ct);

    Task<IReadOnlyList<TableInfo>> GetSchemaAsync(string connectionString, int commandTimeoutSeconds, CancellationToken ct);
}

public interface IDbAdapterFactory
{
    IDbAdapter Get(DatabaseType databaseType);
}
