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
            int recordsAffected;

            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                for (var i = 0; i < reader.FieldCount; i++)
                    columns.Add(reader.GetName(i));

                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count >= limits.MaxRows)
                    {
                        truncated = true;
                        break;
                    }
                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < row.Length; i++)
                        row[i] = ToJsonFriendly(reader.GetValue(i));
                    rows.Add(row);
                }

                // Step through any further result sets. Errors from later statements only surface here;
                // without this, disposing the reader would swallow them and the call would look successful.
                while (await reader.NextResultAsync(ct)) { }

                recordsAffected = reader.RecordsAffected;
            }

            // Read-only: roll back whatever happened, as a second line of defence after the read-only mode.
            if (accessLevel == AccessLevel.ReadOnly)
                await tx.RollbackAsync(ct);
            else
                await tx.CommitAsync(ct);

            return new QueryResult(columns, rows, truncated, recordsAffected, sw.ElapsedMilliseconds);
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
