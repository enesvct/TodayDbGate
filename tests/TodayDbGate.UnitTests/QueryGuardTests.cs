using TodayDbGate.Application.Exceptions;
using TodayDbGate.Application.Security;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.UnitTests;

public class QueryGuardTests
{
    private const AccessLevel RO = AccessLevel.ReadOnly;
    private const AccessLevel RW = AccessLevel.ReadWrite;

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("  select 1;  ")]
    [InlineData("WITH x AS (SELECT 1) SELECT * FROM x")]
    [InlineData("(SELECT 1) UNION (SELECT 2)")]
    [InlineData("SELECT 'a;b' AS v")]
    [InlineData("SELECT 1 -- trailing; comment")]
    [InlineData("/* leading; comment */ SELECT 1")]
    [InlineData("EXPLAIN SELECT 1")]
    public void Allows_single_reading_statements(string sql) =>
        QueryGuard.EnsureAllowed(sql, RO, DatabaseType.PostgreSql);

    [Theory]
    [InlineData("DELETE FROM users")]
    [InlineData("update users set x = 1")]
    [InlineData("DROP TABLE users")]
    [InlineData("/* SELECT */ DELETE FROM users")]
    [InlineData("-- SELECT\nDELETE FROM users")]
    [InlineData("CALL do_something()")]
    public void Rejects_writes_for_read_only(string sql) =>
        Assert.Throws<QueryRejectedException>(() => QueryGuard.EnsureAllowed(sql, RO, DatabaseType.PostgreSql));

    [Fact]
    public void Allows_writes_for_read_write() =>
        QueryGuard.EnsureAllowed("INSERT INTO t (a) VALUES (1)", RW, DatabaseType.PostgreSql);

    [Theory]
    [InlineData("SELECT 1; DELETE FROM users")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("")]
    [InlineData("   ;  ")]
    [InlineData("-- only a comment")]
    public void Rejects_multiple_or_empty_statements_at_any_level(string sql)
    {
        Assert.Throws<QueryRejectedException>(() => QueryGuard.EnsureAllowed(sql, RO, DatabaseType.PostgreSql));
        Assert.Throws<QueryRejectedException>(() => QueryGuard.EnsureAllowed(sql, RW, DatabaseType.PostgreSql));
    }

    // Tricks where the guard's idea of "inside a string" could differ from the database's.

    [Fact]
    public void Postgres_dollar_quotes_cannot_hide_a_second_statement() =>
        Assert.Throws<QueryRejectedException>(() =>
            QueryGuard.EnsureAllowed("SELECT $$ ' $$; DELETE FROM t; -- '", RO, DatabaseType.PostgreSql));

    [Fact]
    public void Postgres_backslash_is_not_an_escape() =>
        Assert.Throws<QueryRejectedException>(() =>
            QueryGuard.EnsureAllowed(@"SELECT 'a\'; DELETE FROM t; --'", RO, DatabaseType.PostgreSql));

    [Fact]
    public void MySql_backslash_escape_cannot_hide_a_second_statement() =>
        Assert.Throws<QueryRejectedException>(() =>
            QueryGuard.EnsureAllowed(@"SELECT 'x\''; DELETE FROM t; -- '", RO, DatabaseType.MySql));

    [Fact]
    public void MySql_double_dash_without_space_is_not_a_comment() =>
        Assert.Throws<QueryRejectedException>(() =>
            QueryGuard.EnsureAllowed("SELECT 1 --1; DELETE FROM t", RO, DatabaseType.MySql));

    [Fact]
    public void MySql_executable_comment_is_not_ignored() =>
        Assert.Throws<QueryRejectedException>(() =>
            QueryGuard.EnsureAllowed("SELECT 1 /*!; DELETE FROM t */", RO, DatabaseType.MySql));

    [Fact]
    public void SqlServer_bracket_identifiers_are_skipped() =>
        QueryGuard.EnsureAllowed("SELECT [weird;name] FROM [dbo].[t]", RO, DatabaseType.SqlServer);
}
