using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Exceptions;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Security;

namespace TodayDbGate.Application.Services;

/// <summary>
/// What an authenticated MCP caller can do: list its connections, read schemas and run queries.
/// Every call is checked against the caller's grants and recorded in the audit log, input and output.
/// </summary>
public sealed class QueryExecutionService(
    IConnectionRepository connections,
    IDbAdapterFactory adapters,
    ISecretProtector protector,
    IAuditSink audit,
    GatewayOptions options,
    TimeProvider time,
    ILogger<QueryExecutionService> logger)
{
    public IReadOnlyList<TokenGrant> ListConnections(TokenPrincipal principal)
    {
        var grants = principal.Grants;
        audit.Record(new AuditEvent(time.GetUtcNow().UtcDateTime, principal.TokenId, principal.Name,
            "list_connections", null, null, AuditStatus.Success, null, 0,
            Output: grants.Select(g => new { g.AliasName, g.DatabaseType, g.AccessLevel }).ToList(),
            RowCount: grants.Count));
        return grants;
    }

    public Task<QueryResult> ExecuteQueryAsync(
        TokenPrincipal principal, string alias, string sql, CancellationToken ct = default) =>
        AuditedAsync(principal, "execute_query", alias, sql, async () =>
        {
            var grant = Resolve(principal, alias);

            if (sql.Length > options.MaxSqlLength)
                throw new QueryRejectedException($"The query is longer than {options.MaxSqlLength} characters.");
            QueryGuard.EnsureAllowed(sql, grant.AccessLevel, grant.DatabaseType);

            var (adapter, connectionString) = await LoadAsync(grant, ct);
            var result = await adapter.ExecuteQueryAsync(
                connectionString, sql, grant.AccessLevel,
                new QueryLimits(options.MaxRows, options.CommandTimeoutSeconds), ct);

            logger.LogInformation(
                "Query executed. Token={Token} Alias={Alias} Access={Access} Rows={Rows} Truncated={Truncated} ElapsedMs={Elapsed}",
                principal.Name, grant.AliasName, grant.AccessLevel, result.Rows.Count, result.Truncated, result.ElapsedMs);
            return result;
        },
        result => (result, result.Rows.Count, result.Truncated, result.RecordsAffected >= 0 ? result.RecordsAffected : null));

    public Task<IReadOnlyList<TableInfo>> DescribeSchemaAsync(
        TokenPrincipal principal, string alias, CancellationToken ct = default) =>
        AuditedAsync(principal, "describe_schema", alias, null, async () =>
        {
            var grant = Resolve(principal, alias);
            var (adapter, connectionString) = await LoadAsync(grant, ct);
            return await adapter.GetSchemaAsync(connectionString, options.CommandTimeoutSeconds, ct);
        },
        tables => (tables, tables.Count, null, null));

    /// <summary>Runs <paramref name="action"/> and records one audit event for it, whatever the outcome.</summary>
    private async Task<T> AuditedAsync<T>(
        TokenPrincipal principal, string tool, string alias, string? input,
        Func<Task<T>> action,
        Func<T, (object? Output, int? Rows, bool? Truncated, int? Affected)> describe)
    {
        var startedAt = time.GetUtcNow().UtcDateTime;
        var sw = Stopwatch.StartNew();

        void Record(AuditStatus status, string? error, object? output = null, int? rows = null, bool? truncated = null, int? affected = null) =>
            audit.Record(new AuditEvent(startedAt, principal.TokenId, principal.Name, tool, alias, input,
                status, error, sw.ElapsedMilliseconds, output, rows, truncated, affected));

        try
        {
            var result = await action();
            var (output, rows, truncated, affected) = describe(result);
            Record(AuditStatus.Success, null, output, rows, truncated, affected);
            return result;
        }
        catch (AccessDeniedException ex)
        {
            Record(AuditStatus.Denied, ex.Message);
            throw;
        }
        catch (QueryRejectedException ex)
        {
            logger.LogWarning("Query rejected. Token={Token} Alias={Alias} Reason={Reason}", principal.Name, alias, ex.Message);
            Record(AuditStatus.Rejected, ex.Message);
            throw;
        }
        catch (GatewayException ex)
        {
            logger.LogWarning(ex, "{Tool} failed. Token={Token} Alias={Alias}", tool, principal.Name, alias);
            // The audit log is admin-only, so it keeps the driver's own message.
            Record(AuditStatus.Failed, ex.InnerException is null ? ex.Message : $"{ex.Message} {ex.InnerException.Message}");
            throw;
        }
        catch (OperationCanceledException)
        {
            Record(AuditStatus.Failed, "Cancelled by the client or timed out.");
            throw;
        }
    }

    private static TokenGrant Resolve(TokenPrincipal principal, string alias) =>
        principal.FindGrant(alias) ?? throw NotAvailable(alias);

    private async Task<(IDbAdapter Adapter, string ConnectionString)> LoadAsync(TokenGrant grant, CancellationToken ct)
    {
        // The grant came from the token lookup; re-read the entry in case it was deleted since.
        var entry = await connections.GetByIdAsync(grant.ConnectionId, ct) ?? throw NotAvailable(grant.AliasName);
        return (adapters.Get(entry.DatabaseType), protector.Unprotect(entry.ConnectionStringEncrypted));
    }

    // Same message whether the alias does not exist or is just not granted, so tokens cannot probe for aliases.
    private static AccessDeniedException NotAvailable(string alias) =>
        new($"Connection '{alias}' is not available for this token.");
}
