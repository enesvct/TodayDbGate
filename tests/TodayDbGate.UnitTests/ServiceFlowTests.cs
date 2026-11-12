using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TodayDbGate.Application;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Exceptions;
using TodayDbGate.Application.Masking;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;
using TodayDbGate.Domain.Enums;
using TodayDbGate.Infrastructure.Adapters;
using TodayDbGate.Infrastructure.Persistence;
using TodayDbGate.Infrastructure.Security;

namespace TodayDbGate.UnitTests;

/// <summary>Services against a real (in-memory) SQLite metadata database; no target databases needed.</summary>
public sealed class ServiceFlowTests : IDisposable
{
    private readonly SqliteConnection _sqlite = new("DataSource=:memory:");
    private readonly GatewayDbContext _db;
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly ISecretProtector _protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
    private readonly DbAdapterFactory _adapters = new([new PostgresAdapter(), new SqlServerAdapter(), new MySqlAdapter()]);
    private readonly ConnectionService _connections;
    private readonly TokenService _tokens;
    private readonly AccessPolicyService _policy;

    public ServiceFlowTests()
    {
        _sqlite.Open();
        _db = new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(_sqlite).Options);
        _db.Database.EnsureCreated();

        var connectionRepo = new ConnectionRepository(_db);
        var tokenRepo = new TokenRepository(_db);
        var hasher = new HmacTokenHasher(RandomNumberGenerator.GetBytes(32));

        _connections = new ConnectionService(connectionRepo, _adapters, _protector, _time);
        _tokens = new TokenService(tokenRepo, connectionRepo, hasher, _time);
        _policy = new AccessPolicyService(tokenRepo, hasher, _time);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqlite.Dispose();
    }

    private async Task<int> AddConnectionAsync(string alias, DatabaseType type = DatabaseType.PostgreSql)
    {
        var result = await _connections.CreateAsync(new ConnectionInput
        {
            AliasName = alias, DatabaseType = type, Host = "db.local", Database = "app",
            Username = "reader", Password = "secret-pw"
        });
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Value;
    }

    private async Task<CreatedToken> AddTokenAsync(params (int ConnectionId, AccessLevel Level)[] grants)
    {
        var result = await _tokens.CreateAsync(new TokenInput
        {
            Name = "claude-desktop",
            Grants = grants.Select(g => new TokenGrantInput { ConnectionId = g.ConnectionId, AccessLevel = g.Level }).ToList()
        });
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Value!;
    }

    [Fact]
    public async Task Connection_secret_is_encrypted_at_rest_and_not_returned_for_edit()
    {
        var id = await AddConnectionAsync("live-postgres-db");

        var stored = await _db.Connections.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("secret-pw", stored.ConnectionStringEncrypted);
        Assert.DoesNotContain("db.local", stored.ConnectionStringEncrypted);

        var edit = await _connections.GetForEditAsync(id);
        Assert.Null(edit!.Password);
        Assert.Equal("db.local", edit.Host);
        Assert.Equal(5432, edit.Port);
    }

    [Fact]
    public async Task Updating_with_empty_password_keeps_the_old_one()
    {
        var id = await AddConnectionAsync("crm");
        var edit = (await _connections.GetForEditAsync(id))!;
        edit.Host = "db2.local";

        Assert.True((await _connections.UpdateAsync(id, edit)).Succeeded);

        var stored = await _db.Connections.AsNoTracking().SingleAsync();
        var details = _adapters.Get(stored.DatabaseType).ParseConnectionString(_protector.Unprotect(stored.ConnectionStringEncrypted));
        Assert.Equal("db2.local", details.Host);
        Assert.Equal("secret-pw", details.Password);
    }

    [Theory]
    [InlineData("Has Spaces")]
    [InlineData("x")]
    [InlineData("-starts-with-dash")]
    public async Task Invalid_aliases_are_rejected(string alias)
    {
        var result = await _connections.CreateAsync(new ConnectionInput
        {
            AliasName = alias, Host = "h", Database = "d", Username = "u", Password = "p"
        });
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Duplicate_alias_is_rejected_case_insensitively()
    {
        await AddConnectionAsync("erp");
        var result = await _connections.CreateAsync(new ConnectionInput
        {
            AliasName = "ERP", Host = "h", Database = "d", Username = "u", Password = "p"
        });
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Token_authenticates_with_its_grants_and_is_stored_only_as_hash()
    {
        var pg = await AddConnectionAsync("live-postgres-db");
        var my = await AddConnectionAsync("shop-mysql", DatabaseType.MySql);
        var created = await AddTokenAsync((pg, AccessLevel.ReadOnly), (my, AccessLevel.ReadWrite));

        var stored = await _db.Tokens.AsNoTracking().SingleAsync();
        Assert.NotEqual(created.PlainToken, stored.TokenHash);
        Assert.StartsWith(stored.TokenPrefix, created.PlainToken);

        var principal = await _policy.AuthenticateAsync(created.PlainToken);
        Assert.NotNull(principal);
        Assert.Equal(AccessLevel.ReadOnly, principal.FindGrant("LIVE-POSTGRES-DB")!.AccessLevel);
        Assert.Equal(AccessLevel.ReadWrite, principal.FindGrant("shop-mysql")!.AccessLevel);
        Assert.Equal(DatabaseType.MySql, principal.FindGrant("shop-mysql")!.DatabaseType);
        Assert.NotNull((await _db.Tokens.AsNoTracking().SingleAsync()).LastUsedAt);
    }

    [Fact]
    public async Task Unknown_inactive_and_expired_tokens_are_rejected()
    {
        var created = await AddTokenAsync();

        Assert.Null(await _policy.AuthenticateAsync(null));
        Assert.Null(await _policy.AuthenticateAsync("tdg_not-a-real-token"));
        Assert.Null(await _policy.AuthenticateAsync(created.PlainToken[4..])); // missing prefix

        await _tokens.SetActiveAsync(created.Id, false);
        Assert.Null(await _policy.AuthenticateAsync(created.PlainToken));

        await _tokens.SetActiveAsync(created.Id, true);
        var update = new TokenInput { Name = "claude-desktop", ExpiresAt = _time.GetUtcNow().UtcDateTime.AddDays(1) };
        Assert.True((await _tokens.UpdateAsync(created.Id, update)).Succeeded);
        Assert.NotNull(await _policy.AuthenticateAsync(created.PlainToken));

        _time.Advance(TimeSpan.FromDays(2));
        Assert.Null(await _policy.AuthenticateAsync(created.PlainToken));
    }

    [Fact]
    public async Task Updating_grants_changes_levels_removes_and_adds()
    {
        var a = await AddConnectionAsync("db-a");
        var b = await AddConnectionAsync("db-b");
        var c = await AddConnectionAsync("db-c");
        var created = await AddTokenAsync((a, AccessLevel.ReadOnly), (b, AccessLevel.ReadOnly));

        var result = await _tokens.UpdateAsync(created.Id, new TokenInput
        {
            Name = "renamed",
            Grants =
            [
                new TokenGrantInput { ConnectionId = a, AccessLevel = AccessLevel.ReadWrite },
                new TokenGrantInput { ConnectionId = c, AccessLevel = AccessLevel.ReadOnly }
            ]
        });
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));

        _db.ChangeTracker.Clear();
        var summary = (await _tokens.GetAsync(created.Id))!;
        Assert.Equal("renamed", summary.Name);
        Assert.Equal(
            [("db-a", AccessLevel.ReadWrite), ("db-c", AccessLevel.ReadOnly)],
            summary.Grants.Select(g => (g.AliasName, g.AccessLevel)).ToList());
    }

    [Fact]
    public async Task Token_cannot_be_granted_a_missing_connection()
    {
        var result = await _tokens.CreateAsync(new TokenInput
        {
            Name = "t", Grants = [new TokenGrantInput { ConnectionId = 999 }]
        });
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Query_execution_enforces_grants_before_touching_the_database()
    {
        var granted = await AddConnectionAsync("granted-db");
        await AddConnectionAsync("other-db");
        var created = await AddTokenAsync((granted, AccessLevel.ReadOnly));
        var principal = (await _policy.AuthenticateAsync(created.PlainToken))!;

        var adapter = Substitute.For<IDbAdapter>();
        adapter.ExecuteQueryAsync(default!, default!, default, default!, default)
            .ReturnsForAnyArgs(new QueryResult(["n"], [[1]], false, -1, 3));
        var factory = Substitute.For<IDbAdapterFactory>();
        factory.Get(Arg.Any<DatabaseType>()).Returns(adapter);

        var audit = Substitute.For<IAuditSink>();
        var service = new QueryExecutionService(new ConnectionRepository(_db), factory, _protector,
            new MaskingRepository(_db), new HmacPseudonymizer(RandomNumberGenerator.GetBytes(32)), audit,
            new GatewayOptions(), _time, NullLogger<QueryExecutionService>.Instance);

        // Not granted, and nonexistent: same error, database never contacted.
        var e1 = await Assert.ThrowsAsync<AccessDeniedException>(() => service.ExecuteQueryAsync(principal, "other-db", "SELECT 1"));
        var e2 = await Assert.ThrowsAsync<AccessDeniedException>(() => service.ExecuteQueryAsync(principal, "nope", "SELECT 1"));
        Assert.Equal(e1.Message.Replace("other-db", "X"), e2.Message.Replace("nope", "X"));

        // Read-only grant: writes rejected before reaching the adapter.
        await Assert.ThrowsAsync<QueryRejectedException>(() => service.ExecuteQueryAsync(principal, "granted-db", "DELETE FROM t"));
        await adapter.DidNotReceiveWithAnyArgs().ExecuteQueryAsync(default!, default!, default, default!, default);

        // Allowed query reaches the adapter with the decrypted connection string and read-only level.
        var result = await service.ExecuteQueryAsync(principal, "granted-db", "SELECT 1");
        Assert.Single(result.Rows);
        await adapter.Received(1).ExecuteQueryAsync(
            Arg.Is<string>(cs => cs.Contains("secret-pw")), "SELECT 1", AccessLevel.ReadOnly,
            Arg.Any<QueryLimits>(), Arg.Any<CancellationToken>());

        // Every call above was audited with its outcome, input and (for the success) its output.
        var events = audit.ReceivedCalls().Select(c => (AuditEvent)c.GetArguments()[0]!).ToList();
        Assert.Equal(
            [AuditStatus.Denied, AuditStatus.Denied, AuditStatus.Rejected, AuditStatus.Success],
            events.Select(e => e.Status));
        Assert.All(events, e => Assert.Equal("execute_query", e.Tool));
        Assert.Equal("DELETE FROM t", events[2].Input);
        Assert.Same(result, events[3].Output);
        Assert.Equal(1, events[3].RowCount);
        Assert.DoesNotContain(events, e => e.Error?.Contains("secret-pw") == true);
    }

    [Fact]
    public async Task Masking_rules_apply_to_results_schema_and_audit_unless_token_may_see_unmasked()
    {
        var connectionId = await AddConnectionAsync("crm-db");
        var masked = await AddTokenAsync((connectionId, AccessLevel.ReadOnly));
        var trusted = await _tokens.CreateAsync(new TokenInput
        {
            Name = "trusted", CanSeeUnmasked = true,
            Grants = [new TokenGrantInput { ConnectionId = connectionId }]
        });

        var origins = new ColumnOrigin?[] { new("public", "customers", "email"), new("public", "customers", "city") };
        var adapter = Substitute.For<IDbAdapter>();
        adapter.ExecuteQueryAsync(default!, default!, default, default!, default).ReturnsForAnyArgs(call =>
            call.ArgAt<string>(1).Contains("payments")
                ? new QueryResult(["amount"], [[10m]], false, -1, 1, [new ColumnOrigin("public", "payments", "amount")])
                : new QueryResult(["contact", "city"], [["ali@example.com", "Ankara"]], false, -1, 1,
                    call.ArgAt<QueryLimits>(3).ResolveColumnOrigins ? origins : null));
        adapter.GetSchemaAsync(default!, default, default).ReturnsForAnyArgs(new List<TableInfo>
        {
            new("public", "customers", [new("email", "text", true), new("city", "text", true)]),
            new("public", "payments", [new("amount", "numeric", false)])
        });
        var factory = Substitute.For<IDbAdapterFactory>();
        factory.Get(Arg.Any<DatabaseType>()).Returns(adapter);

        var maskingRepo = new MaskingRepository(_db);
        var admin = new MaskingAdminService(new ConnectionRepository(_db), maskingRepo, factory, _protector, new GatewayOptions());
        var saved = await admin.SaveAsync(connectionId, new MaskingInput
        {
            DetectPersonalData = true,
            Rules =
            [
                new MaskingRuleInput { Schema = "public", Table = "customers", Column = "email", Action = MaskAction.Partial },
                new MaskingRuleInput { Schema = "public", Table = "payments", Action = MaskAction.Block }
            ]
        });
        Assert.True(saved.Succeeded, string.Join(", ", saved.Errors));

        var audit = Substitute.For<IAuditSink>();
        var service = new QueryExecutionService(new ConnectionRepository(_db), factory, _protector, maskingRepo,
            new HmacPseudonymizer(RandomNumberGenerator.GetBytes(32)), audit, new GatewayOptions(), _time,
            NullLogger<QueryExecutionService>.Instance);

        var principal = (await _policy.AuthenticateAsync(masked.PlainToken))!;
        var result = await service.ExecuteQueryAsync(principal, "crm-db", "SELECT email AS contact, city FROM customers");
        Assert.Equal("a***@example.com", result.Rows[0][0]);
        Assert.Equal("Ankara", result.Rows[0][1]);
        Assert.Equal(["contact"], result.MaskedColumns);

        // What the audit log keeps is what the client saw.
        var audited = (QueryResult)audit.ReceivedCalls().Select(c => (AuditEvent)c.GetArguments()[0]!).Last().Output!;
        Assert.Equal("a***@example.com", audited.Rows[0][0]);

        await Assert.ThrowsAsync<AccessDeniedException>(() => service.ExecuteQueryAsync(principal, "crm-db", "SELECT amount FROM payments"));

        var schema = await service.DescribeSchemaAsync(principal, "crm-db");
        var table = Assert.Single(schema); // payments is hidden
        Assert.Equal("partial", table.Columns.Single(c => c.Name == "email").Masking);
        Assert.Null(table.Columns.Single(c => c.Name == "city").Masking);

        var trustedPrincipal = (await _policy.AuthenticateAsync(trusted.Value!.PlainToken))!;
        Assert.True(trustedPrincipal.CanSeeUnmasked);
        var raw = await service.ExecuteQueryAsync(trustedPrincipal, "crm-db", "SELECT email AS contact, city FROM customers");
        Assert.Equal("ali@example.com", raw.Rows[0][0]);
        Assert.Equal(2, (await service.DescribeSchemaAsync(trustedPrincipal, "crm-db")).Count);
    }

    [Fact]
    public async Task Masking_admin_rejects_invalid_actions_and_merges_schema_with_rules()
    {
        var connectionId = await AddConnectionAsync("shop");
        var adapter = Substitute.For<IDbAdapter>();
        adapter.GetSchemaAsync(default!, default, default).ReturnsForAnyArgs(new List<TableInfo>
        {
            new("public", "customers", [new("id", "int", false), new("email", "text", true)])
        });
        var factory = Substitute.For<IDbAdapterFactory>();
        factory.Get(Arg.Any<DatabaseType>()).Returns(adapter);
        var admin = new MaskingAdminService(new ConnectionRepository(_db), new MaskingRepository(_db), factory, _protector, new GatewayOptions());

        var invalid = await admin.SaveAsync(connectionId, new MaskingInput
        {
            Rules = [new MaskingRuleInput { Table = "customers", Column = "email", Action = MaskAction.Block }]
        });
        Assert.False(invalid.Succeeded);

        await admin.SaveAsync(connectionId, new MaskingInput
        {
            DetectPersonalData = false,
            Rules =
            [
                new MaskingRuleInput { Schema = "public", Table = "customers", Column = "email", Action = MaskAction.Pseudonym },
                new MaskingRuleInput { Schema = "public", Table = "old_table", Column = "ssn", Action = MaskAction.Redact }
            ]
        });

        var editor = (await admin.GetEditorAsync(connectionId))!;
        Assert.False(editor.DetectPersonalData);
        var customers = editor.Tables.Single(t => t.Name == "customers");
        Assert.Equal(MaskAction.Pseudonym, customers.Columns.Single(c => c.Name == "email").Action);
        Assert.Equal(MaskAction.Partial, customers.Columns.Single(c => c.Name == "email").Suggested);
        Assert.True(editor.Tables.Single(t => t.Name == "old_table").Missing); // rule kept and flagged
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
