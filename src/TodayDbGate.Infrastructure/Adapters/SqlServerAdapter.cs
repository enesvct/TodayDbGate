using System.Data.Common;
using Microsoft.Data.SqlClient;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Infrastructure.Adapters;

/// <summary>
/// SQL Server has no read-only transaction mode. Read-only grants rely on the transaction always being
/// rolled back and, above all, on the connection using a login with only db_datareader rights.
/// </summary>
public sealed class SqlServerAdapter : DbAdapterBase
{
    public override DatabaseType DatabaseType => DatabaseType.SqlServer;
    public override int DefaultPort => 1433;

    public override string BuildConnectionString(ConnectionDetails d) => new SqlConnectionStringBuilder
    {
        DataSource = d.Port == DefaultPort ? d.Host : $"{d.Host},{d.Port}",
        InitialCatalog = d.Database,
        UserID = d.Username,
        Password = d.Password,
        Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = d.TrustServerCertificate,
        ConnectTimeout = ConnectTimeoutSeconds,
        ApplicationName = "TodayDbGate"
    }.ConnectionString;

    public override ConnectionDetails ParseConnectionString(string connectionString)
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        var dataSource = b.DataSource ?? "";
        if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) dataSource = dataSource[4..];

        var host = dataSource;
        var port = DefaultPort;
        var comma = dataSource.LastIndexOf(',');
        if (comma > 0 && int.TryParse(dataSource[(comma + 1)..], out var parsed))
        {
            host = dataSource[..comma];
            port = parsed;
        }

        return new ConnectionDetails(host, port, b.InitialCatalog ?? "", b.UserID ?? "", b.Password ?? "", b.TrustServerCertificate);
    }

    protected override DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    protected override string SchemaQuery => """
        SELECT TABLE_SCHEMA AS [Schema], TABLE_NAME AS [Table], COLUMN_NAME AS [Column],
               DATA_TYPE AS DataType, IS_NULLABLE AS IsNullable
        FROM INFORMATION_SCHEMA.COLUMNS
        ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
        """;
}
