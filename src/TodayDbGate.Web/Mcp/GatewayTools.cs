using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TodayDbGate.Application.Exceptions;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;

namespace TodayDbGate.Web.Mcp;

/// <summary>
/// The tools an LLM sees. Every call runs as the token from the request's Authorization header and is
/// limited to that token's grants. Errors are returned as tool errors with safe messages only.
/// </summary>
[McpServerToolType]
public sealed class GatewayTools(QueryExecutionService queries, IHttpContextAccessor httpContextAccessor)
{
    private const int MaxTablesListed = 300;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    [McpServerTool(Name = "list_connections", Title = "List database connections", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the databases this token may use: alias, database type (PostgreSql, SqlServer, MySql) and access level " +
                 "(ReadOnly or ReadWrite). Call this first; every other tool takes one of these aliases.")]
    public string ListConnections()
    {
        var grants = queries.ListConnections(GetPrincipal())
            .Select(g => new { alias = g.AliasName, databaseType = g.DatabaseType.ToString(), access = g.AccessLevel.ToString() });
        return JsonSerializer.Serialize(grants, Json);
    }

    [McpServerTool(Name = "describe_schema", Title = "Describe database schema", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists tables and their columns for a connection, one table per line: " +
                 "'schema.table: column type, column type NULL, ...'. Use it before writing queries. " +
                 "Columns marked [masked: ...] are returned masked by policy; their real values cannot be retrieved.")]
    public async Task<string> DescribeSchema(
        [Description("Connection alias from list_connections.")] string alias,
        [Description("Optional: only tables whose name contains this text (case-insensitive). Use it on large databases.")]
        string? tableNameContains = null,
        CancellationToken cancellationToken = default)
    {
        var tables = await Run(() => queries.DescribeSchemaAsync(GetPrincipal(), alias, cancellationToken));

        if (!string.IsNullOrWhiteSpace(tableNameContains))
            tables = tables.Where(t => t.Name.Contains(tableNameContains.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        if (tables.Count == 0)
            return "No tables found.";

        var sb = new StringBuilder();
        foreach (var table in tables.Take(MaxTablesListed))
        {
            sb.Append(table.Schema).Append('.').Append(table.Name).Append(": ");
            sb.AppendJoin(", ", table.Columns.Select(c =>
                $"{c.Name} {c.DataType}{(c.IsNullable ? " NULL" : "")}{(c.Masking is null ? "" : $" [masked: {c.Masking}]")}"));
            sb.AppendLine();
        }
        if (tables.Count > MaxTablesListed)
            sb.AppendLine($"... {tables.Count - MaxTablesListed} more tables not shown. Use tableNameContains to narrow the list.");

        return sb.ToString();
    }

    [McpServerTool(Name = "execute_query", Title = "Execute SQL query", OpenWorld = false)]
    [Description("Runs one SQL statement on a connection and returns JSON: columns, rows (arrays in column order), rowCount, " +
                 "truncated (true when the row limit was hit; add filters or LIMIT/TOP and try again) and recordsAffected for writes. " +
                 "Use the SQL dialect of the connection's database type. Only one statement per call. " +
                 "ReadOnly connections accept only reading statements (SELECT, WITH, SHOW, EXPLAIN); writes are refused. " +
                 "Personal or sensitive values may be masked by policy (e.g. a***@example.com, email_3f9a1c2b7d, [REDACTED]); " +
                 "maskedColumns lists the affected columns. Masked values are final: do not try to work around them. " +
                 "Pseudonyms such as email_3f9a1c2b7d are stable, so they can still be compared, grouped and counted.")]
    public async Task<string> ExecuteQuery(
        [Description("Connection alias from list_connections.")] string alias,
        [Description("A single SQL statement.")] string sql,
        CancellationToken cancellationToken = default)
    {
        var result = await Run(() => queries.ExecuteQueryAsync(GetPrincipal(), alias, sql, cancellationToken));

        return JsonSerializer.Serialize(new
        {
            columns = result.Columns,
            rows = result.Rows,
            rowCount = result.Rows.Count,
            truncated = result.Truncated,
            recordsAffected = result.RecordsAffected >= 0 ? result.RecordsAffected : (int?)null,
            maskedColumns = result.MaskedColumns is { Count: > 0 } ? result.MaskedColumns : null,
            elapsedMs = result.ElapsedMs
        }, Json);
    }

    private TokenPrincipal GetPrincipal() =>
        httpContextAccessor.HttpContext?.Items[typeof(TokenPrincipal)] as TokenPrincipal
        ?? throw new McpException("Not authenticated.");

    /// <summary>Turns gateway errors into tool errors the model can read. Their messages are safe by design.</summary>
    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (GatewayException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
