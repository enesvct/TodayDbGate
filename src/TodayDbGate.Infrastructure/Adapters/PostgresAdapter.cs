using System.Data.Common;
using Npgsql;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Infrastructure.Adapters;

public sealed class PostgresAdapter : DbAdapterBase
{
    public override DatabaseType DatabaseType => DatabaseType.PostgreSql;
    public override int DefaultPort => 5432;

    public override string BuildConnectionString(ConnectionDetails d) => new NpgsqlConnectionStringBuilder
    {
        Host = d.Host,
        Port = d.Port,
        Database = d.Database,
        Username = d.Username,
        Password = d.Password,
        Timeout = ConnectTimeoutSeconds,
        ApplicationName = "TodayDbGate"
    }.ConnectionString;

    public override ConnectionDetails ParseConnectionString(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        return new ConnectionDetails(b.Host ?? "", b.Port, b.Database ?? "", b.Username ?? "", b.Password ?? "");
    }

    protected override DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    protected override async Task<DbTransaction> BeginTransactionAsync(
        DbConnection connection, AccessLevel accessLevel, CancellationToken ct)
    {
        var tx = await connection.BeginTransactionAsync(ct);
        if (accessLevel == AccessLevel.ReadOnly)
        {
            // PostgreSQL then refuses any write (INSERT/UPDATE/DELETE/DDL) inside this transaction.
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SET TRANSACTION READ ONLY";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return tx;
    }

    protected override string SchemaQuery => """
        SELECT table_schema AS "Schema", table_name AS "Table", column_name AS "Column",
               data_type AS "DataType", is_nullable AS "IsNullable"
        FROM information_schema.columns
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name, ordinal_position
        """;
}
