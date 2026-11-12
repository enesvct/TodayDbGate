using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Exceptions;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Security;
using TodayDbGate.Domain.Enums;
using TodayDbGate.Infrastructure.Adapters;

namespace TodayDbGate.IntegrationTests;

/// <summary>Runs only when the given environment variable holds a connection string; skipped otherwise.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute(string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable)))
            Skip = $"Set {environmentVariable} to a connection string to run this test.";
    }
}

/// <summary>
/// Adapters against real databases. Example (PowerShell):
///   docker run -d --name tdg-pg -e POSTGRES_PASSWORD=adminpw -p 55432:5432 postgres:17-alpine
///   $env:TDG_IT_POSTGRES = "Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=adminpw"
///   docker run -d --name tdg-mysql -e MYSQL_ROOT_PASSWORD=adminpw -e MYSQL_DATABASE=app -p 53306:3306 mysql:8.4
///   $env:TDG_IT_MYSQL = "Server=localhost;Port=53306;Database=app;User ID=root;Password=adminpw"
/// </summary>
public class AdapterIntegrationTests
{
    private const string PgVar = "TDG_IT_POSTGRES";
    private const string MyVar = "TDG_IT_MYSQL";
    private static readonly QueryLimits Limits = new(MaxRows: 2, CommandTimeoutSeconds: 15);

    private static string Cs(string variable) => Environment.GetEnvironmentVariable(variable)!;

    /// <summary>Creates a fresh table with three rows and returns its name.</summary>
    private static async Task<string> CreateTableAsync(IDbAdapter adapter, string cs)
    {
        var table = "it_items_" + Guid.NewGuid().ToString("N")[..8];
        await adapter.ExecuteQueryAsync(cs, $"CREATE TABLE {table} (id INT PRIMARY KEY, name VARCHAR(50))", AccessLevel.ReadWrite, Limits, default);
        await adapter.ExecuteQueryAsync(cs, $"INSERT INTO {table} VALUES (1,'a'),(2,'b'),(3,'c')", AccessLevel.ReadWrite, Limits, default);
        return table;
    }

    private static async Task<long> CountAsync(IDbAdapter adapter, string cs, string table)
    {
        var r = await adapter.ExecuteQueryAsync(cs, $"SELECT COUNT(*) FROM {table}", AccessLevel.ReadOnly, Limits, default);
        return Convert.ToInt64(r.Rows[0][0]);
    }

    private static async Task DropAsync(IDbAdapter adapter, string cs, string table) =>
        await adapter.ExecuteQueryAsync(cs, $"DROP TABLE {table}", AccessLevel.ReadWrite, Limits, default);

    [IntegrationFact(PgVar)]
    public async Task Postgres_read_only_reads_truncates_and_blocks_writes()
    {
        var adapter = new PostgresAdapter();
        var cs = Cs(PgVar);
        var table = await CreateTableAsync(adapter, cs);
        try
        {
            var result = await adapter.ExecuteQueryAsync(cs, $"SELECT id, name FROM {table} ORDER BY id", AccessLevel.ReadOnly, Limits, default);
            Assert.Equal(["id", "name"], result.Columns);
            Assert.Equal(2, result.Rows.Count);
            Assert.True(result.Truncated);

            // MaxRows = 0 turns the limit off: every row comes back.
            var all = await adapter.ExecuteQueryAsync(cs, $"SELECT id FROM {table}", AccessLevel.ReadOnly, Limits with { MaxRows = 0 }, default);
            Assert.Equal(3, all.Rows.Count);
            Assert.False(all.Truncated);

            await Assert.ThrowsAsync<QueryFailedException>(() =>
                adapter.ExecuteQueryAsync(cs, $"INSERT INTO {table} VALUES (9,'x')", AccessLevel.ReadOnly, Limits, default));

            // A data-modifying CTE starts with WITH, so the text guard lets it through...
            var sneaky = $"WITH d AS (DELETE FROM {table} RETURNING *) SELECT * FROM d";
            QueryGuard.EnsureAllowed(sneaky, AccessLevel.ReadOnly, DatabaseType.PostgreSql);
            // ...but the read-only transaction stops it.
            var ex = await Assert.ThrowsAsync<QueryFailedException>(() =>
                adapter.ExecuteQueryAsync(cs, sneaky, AccessLevel.ReadOnly, Limits, default));
            Assert.Contains("read-only", ex.Message);

            Assert.Equal(3, await CountAsync(adapter, cs, table));

            var write = await adapter.ExecuteQueryAsync(cs, $"INSERT INTO {table} VALUES (4,'d')", AccessLevel.ReadWrite, Limits, default);
            Assert.Equal(1, write.RecordsAffected);
            Assert.Equal(4, await CountAsync(adapter, cs, table));

            var schema = await adapter.GetSchemaAsync(cs, 15, default);
            var info = Assert.Single(schema, t => t.Name == table);
            Assert.Equal(["id", "name"], info.Columns.Select(c => c.Name));
            Assert.False(info.Columns[0].IsNullable);
        }
        finally
        {
            await DropAsync(adapter, cs, table);
        }
    }

    [IntegrationFact(PgVar)]
    public async Task Postgres_row_limit_stops_the_server_instead_of_draining_the_rest()
    {
        var adapter = new PostgresAdapter();
        var cs = Cs(PgVar);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 100 million rows that stream like a table scan (a single generate_series would be built in full
        // before the first row is sent); with MaxRows = 10 only the first 10 are needed.
        var result = await adapter.ExecuteQueryAsync(cs, "SELECT a FROM generate_series(1, 10000) a, generate_series(1, 10000) b",
            AccessLevel.ReadOnly, new QueryLimits(MaxRows: 10, CommandTimeoutSeconds: 120), default);

        Assert.Equal(10, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed.TotalSeconds:0.0} s");

        // The pooled connection is still healthy afterwards.
        var next = await adapter.ExecuteQueryAsync(cs, "SELECT 1", AccessLevel.ReadOnly, Limits, default);
        Assert.Single(next.Rows);
    }

    [IntegrationFact(MyVar)]
    public async Task MySql_read_only_reads_truncates_and_blocks_writes()
    {
        var adapter = new MySqlAdapter();
        var cs = Cs(MyVar);
        var table = await CreateTableAsync(adapter, cs);
        try
        {
            var result = await adapter.ExecuteQueryAsync(cs, $"SELECT id, name FROM {table} ORDER BY id", AccessLevel.ReadOnly, Limits, default);
            Assert.Equal(2, result.Rows.Count);
            Assert.True(result.Truncated);

            await Assert.ThrowsAsync<QueryFailedException>(() =>
                adapter.ExecuteQueryAsync(cs, $"DELETE FROM {table}", AccessLevel.ReadOnly, Limits, default));

            // Even if a second statement got past the guard, the read-only transaction refuses it.
            await Assert.ThrowsAsync<QueryFailedException>(() =>
                adapter.ExecuteQueryAsync(cs, $"SELECT 1; DELETE FROM {table}", AccessLevel.ReadOnly, Limits, default));

            Assert.Equal(3, await CountAsync(adapter, cs, table));

            var schema = await adapter.GetSchemaAsync(cs, 15, default);
            Assert.Contains(schema, t => t.Name == table && t.Columns.Count == 2);
        }
        finally
        {
            await DropAsync(adapter, cs, table);
        }
    }

    [IntegrationFact(MyVar)]
    public async Task MySql_row_limit_stops_the_server_instead_of_draining_the_rest()
    {
        var adapter = new MySqlAdapter();
        var cs = Cs(MyVar);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // A billion rows that stream (joining information_schema views would spend ~30 s before the first row).
        var result = await adapter.ExecuteQueryAsync(cs,
            "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1000) SELECT a.i FROM n a, n b, n c",
            AccessLevel.ReadOnly, new QueryLimits(MaxRows: 10, CommandTimeoutSeconds: 120), default);

        Assert.Equal(10, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed.TotalSeconds:0.0} s");
        Assert.Single((await adapter.ExecuteQueryAsync(cs, "SELECT 1", AccessLevel.ReadOnly, Limits, default)).Rows);
    }

    // ---- SQL Server ----
    // docker run -d --name tdg-mssql -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=Gateway!Test2026" -p 51433:1433 mcr.microsoft.com/mssql/server:2022-latest
    // $env:TDG_IT_SQLSERVER = "Server=localhost,51433;User ID=sa;Password=Gateway!Test2026;TrustServerCertificate=true"

    private const string MsVar = "TDG_IT_SQLSERVER";
    private const string MsReaderPassword = "Reader!Test2026";

    /// <summary>
    /// Creates database tdg_it and a login that is only in db_datareader, the setup we recommend.
    /// Returns (admin, reader) connection strings built by the adapter itself.
    /// </summary>
    private static async Task<(string Admin, string Reader)> SqlServerSetupAsync()
    {
        var adapter = new SqlServerAdapter();
        var sa = adapter.ParseConnectionString(Cs(MsVar));

        // DDL like CREATE DATABASE cannot run inside the adapter's transaction; use a plain connection.
        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(Cs(MsVar)))
        {
            await connection.OpenAsync();
            foreach (var sql in new[]
            {
                "IF DB_ID('tdg_it') IS NULL CREATE DATABASE tdg_it",
                $"IF SUSER_ID('tdg_reader') IS NULL CREATE LOGIN tdg_reader WITH PASSWORD = '{MsReaderPassword}', CHECK_POLICY = OFF",
                "USE tdg_it; IF USER_ID('tdg_reader') IS NULL BEGIN CREATE USER tdg_reader FOR LOGIN tdg_reader; ALTER ROLE db_datareader ADD MEMBER tdg_reader; END"
            })
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var admin = sa with { Database = "tdg_it" };
        var reader = admin with { Username = "tdg_reader", Password = MsReaderPassword };
        return (adapter.BuildConnectionString(admin), adapter.BuildConnectionString(reader));
    }

    [IntegrationFact(MsVar)]
    public async Task SqlServer_reads_truncates_and_never_keeps_writes_for_read_only()
    {
        var adapter = new SqlServerAdapter();
        var (admin, reader) = await SqlServerSetupAsync();
        var table = await CreateTableAsync(adapter, admin);
        try
        {
            var result = await adapter.ExecuteQueryAsync(reader, $"SELECT id, name FROM {table} ORDER BY id", AccessLevel.ReadOnly, Limits, default);
            Assert.Equal(["id", "name"], result.Columns);
            Assert.Equal(2, result.Rows.Count);
            Assert.True(result.Truncated);

            // Read-only login: the database itself refuses the write.
            var denied = await Assert.ThrowsAsync<QueryFailedException>(() =>
                adapter.ExecuteQueryAsync(reader, $"INSERT INTO {table} VALUES (9,'x')", AccessLevel.ReadOnly, Limits, default));
            Assert.Contains("permission", denied.Message, StringComparison.OrdinalIgnoreCase);

            // A second statement's error is reported, not swallowed.
            await Assert.ThrowsAsync<QueryFailedException>(() =>
                adapter.ExecuteQueryAsync(reader, $"SELECT 1; DELETE FROM {table}", AccessLevel.ReadOnly, Limits, default));

            // SQL Server has no read-only transaction mode. Even with a login that CAN write,
            // a read-only grant's transaction is always rolled back, so nothing is kept.
            await adapter.ExecuteQueryAsync(admin, $"INSERT INTO {table} VALUES (8,'y')", AccessLevel.ReadOnly, Limits, default);
            await adapter.ExecuteQueryAsync(admin, $"DELETE FROM {table}", AccessLevel.ReadOnly, Limits, default);
            Assert.Equal(3, await CountAsync(adapter, admin, table));

            // Read-write commits.
            await adapter.ExecuteQueryAsync(admin, $"INSERT INTO {table} VALUES (4,'d')", AccessLevel.ReadWrite, Limits, default);
            Assert.Equal(4, await CountAsync(adapter, admin, table));

            var schema = await adapter.GetSchemaAsync(reader, 15, default);
            var info = Assert.Single(schema, t => t.Name == table);
            Assert.Equal(["id", "name"], info.Columns.Select(c => c.Name));
        }
        finally
        {
            await DropAsync(adapter, admin, table);
        }
    }

    [IntegrationFact(MsVar)]
    public async Task SqlServer_row_limit_stops_the_server_instead_of_draining_the_rest()
    {
        var adapter = new SqlServerAdapter();
        var (_, reader) = await SqlServerSetupAsync();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await adapter.ExecuteQueryAsync(reader,
            "SELECT TOP (100000000) a.object_id FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c",
            AccessLevel.ReadOnly, new QueryLimits(MaxRows: 10, CommandTimeoutSeconds: 120), default);

        Assert.Equal(10, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed.TotalSeconds:0.0} s");
        Assert.Single((await adapter.ExecuteQueryAsync(reader, "SELECT 1", AccessLevel.ReadOnly, Limits, default)).Rows);
    }

    [IntegrationFact(MsVar)]
    public async Task SqlServer_wrong_password_gives_database_unavailable()
    {
        var adapter = new SqlServerAdapter();
        var details = adapter.ParseConnectionString(Cs(MsVar)) with { Password = "wrong" };

        var ex = await Assert.ThrowsAsync<DatabaseUnavailableException>(() =>
            adapter.TestConnectionAsync(adapter.BuildConnectionString(details), default));
        Assert.Equal("Could not connect to the database.", ex.Message);
    }

    // ---- Column origins (what masking rules are matched against) ----

    /// <summary>Aliases and joins resolve to the real table/column; computed columns have no origin.</summary>
    private static async Task AssertColumnOriginsAsync(IDbAdapter adapter, string cs)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        string people = "it_people_" + suffix, orders = "it_orders_" + suffix;
        var rw = Limits with { MaxRows = 0 };
        await adapter.ExecuteQueryAsync(cs, $"CREATE TABLE {people} (id INT PRIMARY KEY, email VARCHAR(100))", AccessLevel.ReadWrite, rw, default);
        await adapter.ExecuteQueryAsync(cs, $"CREATE TABLE {orders} (id INT PRIMARY KEY, person_id INT)", AccessLevel.ReadWrite, rw, default);
        await adapter.ExecuteQueryAsync(cs, $"INSERT INTO {people} VALUES (1, 'a@example.com')", AccessLevel.ReadWrite, rw, default);
        await adapter.ExecuteQueryAsync(cs, $"INSERT INTO {orders} VALUES (10, 1)", AccessLevel.ReadWrite, rw, default);
        try
        {
            var limits = new QueryLimits(0, 15, ResolveColumnOrigins: true);

            var aliased = await adapter.ExecuteQueryAsync(cs, $"SELECT email AS contact FROM {people}", AccessLevel.ReadOnly, limits, default);
            Assert.Equal(["contact"], aliased.Columns); // SQL Server's hidden key columns must not leak
            Assert.Single(aliased.Rows[0]);
            Assert.Equal((people, "email"), (aliased.Origins![0]!.Table.ToLowerInvariant(), aliased.Origins[0]!.Column.ToLowerInvariant()));

            var joined = await adapter.ExecuteQueryAsync(cs,
                $"SELECT x.email, y.id FROM {people} x JOIN {orders} y ON y.person_id = x.id", AccessLevel.ReadOnly, limits, default);
            Assert.Equal(2, joined.Columns.Count);
            Assert.Equal(people, joined.Origins![0]!.Table.ToLowerInvariant());
            Assert.Equal(orders, joined.Origins[1]!.Table.ToLowerInvariant());

            var computed = await adapter.ExecuteQueryAsync(cs, $"SELECT UPPER(email) AS up FROM {people}", AccessLevel.ReadOnly, limits, default);
            Assert.Null(computed.Origins![0]);

            // Without the flag nothing extra is fetched.
            Assert.Null((await adapter.ExecuteQueryAsync(cs, $"SELECT email FROM {people}", AccessLevel.ReadOnly, Limits, default)).Origins);
        }
        finally
        {
            await adapter.ExecuteQueryAsync(cs, $"DROP TABLE {orders}", AccessLevel.ReadWrite, rw, default);
            await adapter.ExecuteQueryAsync(cs, $"DROP TABLE {people}", AccessLevel.ReadWrite, rw, default);
        }
    }

    [IntegrationFact(PgVar)]
    public Task Postgres_reports_column_origins() => AssertColumnOriginsAsync(new PostgresAdapter(), Cs(PgVar));

    [IntegrationFact(MyVar)]
    public Task MySql_reports_column_origins() => AssertColumnOriginsAsync(new MySqlAdapter(), Cs(MyVar));

    [IntegrationFact(MsVar)]
    public async Task SqlServer_reports_column_origins()
    {
        var (admin, _) = await SqlServerSetupAsync();
        await AssertColumnOriginsAsync(new SqlServerAdapter(), admin);
    }

    [IntegrationFact(PgVar)]
    public async Task Wrong_password_gives_database_unavailable_with_details_only_inside()
    {
        var adapter = new PostgresAdapter();
        var details = adapter.ParseConnectionString(Cs(PgVar)) with { Password = "wrong" };

        var ex = await Assert.ThrowsAsync<DatabaseUnavailableException>(() =>
            adapter.TestConnectionAsync(adapter.BuildConnectionString(details), default));

        Assert.Equal("Could not connect to the database.", ex.Message);
        Assert.NotNull(ex.InnerException);
    }
}
