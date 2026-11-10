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
