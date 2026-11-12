using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Dapper;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Exceptions;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Infrastructure.Adapters;

/// <summary>Shared ADO.NET logic; each database only supplies its driver, connection string format and schema query.</summary>
public abstract class DbAdapterBase : IDbAdapter
{
    /// <summary>Seconds to wait when opening a connection.</summary>
    protected const int ConnectTimeoutSeconds = 10;

    public abstract DatabaseType DatabaseType { get; }
    public abstract int DefaultPort { get; }
    public abstract string BuildConnectionString(ConnectionDetails details);
    public abstract ConnectionDetails ParseConnectionString(string connectionString);

    protected abstract DbConnection CreateConnection(string connectionString);

    /// <summary>
    /// Must return columns named Schema, Table, Column, DataType, IsNullable ('YES'/'NO'),
    /// ordered by schema, table and column position.
    /// </summary>
    protected abstract string SchemaQuery { get; }

    /// <summary>
    /// Reader behaviour that makes the driver report the base table and column of each result column.
    /// MySQL always sends it; PostgreSQL (Npgsql) and SQL Server need KeyInfo.
    /// </summary>
    protected virtual CommandBehavior OriginBehavior => CommandBehavior.Default;

    /// <summary>
    /// Origin of each visible result column as the driver reports it: aliases, joins, subqueries and CTEs
    /// resolve to the real table; computed columns and UNIONs have none (null).
    /// </summary>
    private static async Task<IReadOnlyList<ColumnOrigin?>> ReadOriginsAsync(DbDataReader reader, int fieldCount, CancellationToken ct)
    {
        var schema = (await reader.GetColumnSchemaAsync(ct)).Where(c => c.IsHidden != true).ToList();
        var origins = new ColumnOrigin?[fieldCount];
        for (var i = 0; i < fieldCount && i < schema.Count; i++)
        {
            var c = schema[i];
            if (string.IsNullOrEmpty(c.BaseTableName)) continue;
            // SQL Server leaves BaseColumnName empty when the column is not aliased.
            var column = string.IsNullOrEmpty(c.BaseColumnName) ? c.ColumnName : c.BaseColumnName;
            origins[i] = new ColumnOrigin(string.IsNullOrEmpty(c.BaseSchemaName) ? null : c.BaseSchemaName, c.BaseTableName, column);
        }
        return origins;
    }

    /// <summary>Starts the transaction a query runs in. Override to put it in read-only mode.</summary>
    protected virtual async Task<DbTransaction> BeginTransactionAsync(
        DbConnection connection, AccessLevel accessLevel, CancellationToken ct) =>
        await connection.BeginTransactionAsync(ct);

    public async Task TestConnectionAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = await OpenAsync(connectionString, ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = ConnectTimeoutSeconds;
            await cmd.ExecuteScalarAsync(ct);
        }
        catch (DbException ex)
        {
            throw new DatabaseUnavailableException(ex);
        }
    }

    public async Task<QueryResult> ExecuteQueryAsync(
        string connectionString, string sql, AccessLevel accessLevel, QueryLimits limits, CancellationToken ct)
    {
        await using var connection = await OpenAsync(connectionString, ct);
        var sw = Stopwatch.StartNew();
        try
        {
            await using var tx = await BeginTransactionAsync(connection, accessLevel, ct);
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.CommandTimeout = limits.CommandTimeoutSeconds;

            var columns = new List<string>();
            var rows = new List<object?[]>();
            var truncated = false;
            var cancelled = false;
            var recordsAffected = -1;

            IReadOnlyList<ColumnOrigin?>? origins = null;

            var reader = await cmd.ExecuteReaderAsync(limits.ResolveColumnOrigins ? OriginBehavior : CommandBehavior.Default, ct);
            try
            {
                // VisibleFieldCount: with KeyInfo, SQL Server appends hidden key columns we must not return.
                var fieldCount = reader.VisibleFieldCount;
                for (var i = 0; i < fieldCount; i++)
                    columns.Add(reader.GetName(i));

                if (limits.ResolveColumnOrigins)
                    origins = await ReadOriginsAsync(reader, fieldCount, ct);

                while (await reader.ReadAsync(ct))
                {
                    if (limits.MaxRows > 0 && rows.Count >= limits.MaxRows)
                    {
                        truncated = true;
                        break;
                    }
                    var row = new object?[fieldCount];
                    for (var i = 0; i < row.Length; i++)
                        row[i] = ToJsonFriendly(reader.GetValue(i));
                    rows.Add(row);
                }

                if (truncated && accessLevel == AccessLevel.ReadOnly)
                {
                    // Without this the driver reads every remaining row just to throw it away, keeping the
                    // database busy until the end or the timeout. Safe only for read-only calls: the transaction
                    // is rolled back anyway. For read-write we keep reading, because cancelling there would
                    // undo a write while the caller is told it succeeded.
                    cmd.Cancel();
                    cancelled = true;
                }
                else
                {
                    // Step through any further result sets. Errors from later statements only surface here;
                    // without this, disposing the reader would swallow them and the call would look successful.
                    while (await reader.NextResultAsync(ct)) { }
                    recordsAffected = reader.RecordsAffected;
                }
            }
            finally
            {
                try { await reader.DisposeAsync(); }
                catch (Exception) when (cancelled) { /* the server reports the cancellation we asked for */ }
            }

            // Read-only: roll back whatever happened, as a second line of defence after the read-only mode.
            if (accessLevel == AccessLevel.ReadOnly)
            {
                try { await tx.RollbackAsync(ct); }
                catch (Exception) when (cancelled) { /* some drivers already ended the transaction on cancel */ }
            }
            else
            {
                await tx.CommitAsync(ct);
            }

            return new QueryResult(columns, rows, truncated, recordsAffected, sw.ElapsedMilliseconds, origins);
        }
        catch (DbException ex)
        {
            throw new QueryFailedException(ex.Message, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not GatewayException)
        {
            throw new QueryFailedException($"Query failed: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<TableInfo>> GetSchemaAsync(string connectionString, int commandTimeoutSeconds, CancellationToken ct)
    {
        await using var connection = await OpenAsync(connectionString, ct);
        try
        {
            var rows = await connection.QueryAsync<SchemaRow>(
                new CommandDefinition(SchemaQuery, commandTimeout: commandTimeoutSeconds, cancellationToken: ct));

            return rows
                .GroupBy(r => (r.Schema, r.Table))
                .Select(g => new TableInfo(g.Key.Schema, g.Key.Table,
                    g.Select(r => new ColumnInfo(r.Column, r.DataType,
                        string.Equals(r.IsNullable, "YES", StringComparison.OrdinalIgnoreCase))).ToList()))
                .ToList();
        }
        catch (DbException ex)
        {
            throw new QueryFailedException(ex.Message, ex);
        }
    }

    private async Task<DbConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = CreateConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await connection.DisposeAsync();
            throw new DatabaseUnavailableException(ex);
        }
    }

    /// <summary>Keeps values that serialise cleanly to JSON; everything else becomes text.</summary>
    private static object? ToJsonFriendly(object? value) => value switch
    {
        null or DBNull => null,
        string or bool or char => value,
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
        DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or Guid => value,
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value.ToString()
    };

    private sealed class SchemaRow
    {
        public string Schema { get; set; } = "";
        public string Table { get; set; } = "";
        public string Column { get; set; } = "";
        public string DataType { get; set; } = "";
        public string IsNullable { get; set; } = "";
    }
}
