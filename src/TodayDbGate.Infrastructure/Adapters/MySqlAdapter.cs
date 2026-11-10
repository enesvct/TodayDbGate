using System.Data;
using System.Data.Common;
using MySqlConnector;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Infrastructure.Adapters;

public sealed class MySqlAdapter : DbAdapterBase
{
    public override DatabaseType DatabaseType => DatabaseType.MySql;
    public override int DefaultPort => 3306;

    public override string BuildConnectionString(ConnectionDetails d) => new MySqlConnectionStringBuilder
    {
        Server = d.Host,
        Port = (uint)d.Port,
        Database = d.Database,
        UserID = d.Username,
        Password = d.Password,
        ConnectionTimeout = ConnectTimeoutSeconds
    }.ConnectionString;

    public override ConnectionDetails ParseConnectionString(string connectionString)
    {
        var b = new MySqlConnectionStringBuilder(connectionString);
        return new ConnectionDetails(b.Server ?? "", (int)b.Port, b.Database ?? "", b.UserID ?? "", b.Password ?? "");
    }

    protected override DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    protected override async Task<DbTransaction> BeginTransactionAsync(
        DbConnection connection, AccessLevel accessLevel, CancellationToken ct)
    {
        var mySql = (MySqlConnection)connection;
        // START TRANSACTION READ ONLY: MySQL then refuses writes to tables inside this transaction.
        return accessLevel == AccessLevel.ReadOnly
            ? await mySql.BeginTransactionAsync(IsolationLevel.RepeatableRead, isReadOnly: true, ct)
            : await mySql.BeginTransactionAsync(ct);
    }

    protected override string SchemaQuery => """
        SELECT TABLE_SCHEMA AS `Schema`, TABLE_NAME AS `Table`, COLUMN_NAME AS `Column`,
               DATA_TYPE AS DataType, IS_NULLABLE AS IsNullable
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY TABLE_NAME, ORDINAL_POSITION
        """;
}
