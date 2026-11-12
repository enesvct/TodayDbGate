using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Exceptions;
using TodayDbGate.Application.Masking;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Entities;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Services;

/// <summary>Dashboard side of masking: show a connection's live schema with its rules, and save rule changes.</summary>
public sealed class MaskingAdminService(
    IConnectionRepository connections,
    IMaskingRepository masking,
    IDbAdapterFactory adapters,
    ISecretProtector protector,
    GatewayOptions options)
{
    private static readonly MaskAction[] TableActions = [MaskAction.Redact, MaskAction.Block];
    private static readonly MaskAction[] ColumnActions = [MaskAction.Allow, MaskAction.Partial, MaskAction.Pseudonym, MaskAction.Redact];

    public async Task<MaskingEditor?> GetEditorAsync(int connectionId, CancellationToken ct = default)
    {
        var entry = await connections.GetByIdAsync(connectionId, ct);
        if (entry is null) return null;

        var rules = await masking.GetRulesAsync(connectionId, ct);

        IReadOnlyList<TableInfo> schema = [];
        string? schemaError = null;
        try
        {
            schema = await adapters.Get(entry.DatabaseType)
                .GetSchemaAsync(protector.Unprotect(entry.ConnectionStringEncrypted), options.CommandTimeoutSeconds, ct);
        }
        catch (GatewayException ex)
        {
            schemaError = ex.InnerException?.Message ?? ex.Message;
        }

        return new MaskingEditor(entry.Id, entry.AliasName, entry.DatabaseType, entry.DetectPersonalData,
            Merge(schema, rules), schemaError);
    }

    public async Task<OperationResult> SaveAsync(int connectionId, MaskingInput input, CancellationToken ct = default)
    {
        if (await connections.GetByIdAsync(connectionId, ct) is null)
            return OperationResult.Failure("Connection not found.");

        var errors = new List<string>();
        var rules = new Dictionary<(string, string, string), MaskingRule>();
        foreach (var r in input.Rules)
        {
            var table = r.Table.Trim();
            var column = r.Column.Trim();
            if (table.Length == 0) continue;

            var allowed = column.Length == 0 ? TableActions : ColumnActions;
            if (!allowed.Contains(r.Action))
            {
                errors.Add($"'{r.Action}' is not valid for {(column.Length == 0 ? "a table" : "a column")} ({table}{(column.Length == 0 ? "" : "." + column)}).");
                continue;
            }

            var key = (r.Schema.Trim().ToLowerInvariant(), table.ToLowerInvariant(), column.ToLowerInvariant());
            rules[key] = new MaskingRule
            {
                ConnectionId = connectionId, SchemaName = r.Schema.Trim(), TableName = table, ColumnName = column, Action = r.Action
            };
        }
        if (errors.Count > 0) return OperationResult.Failure(errors);

        await masking.SaveAsync(connectionId, input.DetectPersonalData, rules.Values.ToList(), ct);
        return OperationResult.Success();
    }

    /// <summary>Live schema plus saved rules; rules whose table or column disappeared are kept and flagged.</summary>
    private static List<MaskingTableView> Merge(IReadOnlyList<TableInfo> schema, List<MaskingRule> rules)
    {
        static string Key(string schemaName, string table) => $"{schemaName}\u0001{table}".ToLowerInvariant();

        var byTable = rules.GroupBy(r => Key(r.SchemaName, r.TableName)).ToDictionary(g => g.Key, g => g.ToList());
        var tables = new List<MaskingTableView>();

        foreach (var t in schema)
        {
            var tableRules = byTable.GetValueOrDefault(Key(t.Schema, t.Name)) ?? [];
            byTable.Remove(Key(t.Schema, t.Name));

            var known = t.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var columns = t.Columns.Select(c => new MaskingColumnView(
                    c.Name, c.DataType,
                    tableRules.FirstOrDefault(r => string.Equals(r.ColumnName, c.Name, StringComparison.OrdinalIgnoreCase))?.Action,
                    SensitiveColumnNames.Suggest(c.Name),
                    Missing: false))
                .Concat(tableRules.Where(r => !r.IsTableRule && !known.Contains(r.ColumnName))
                    .Select(r => new MaskingColumnView(r.ColumnName, "?", r.Action, null, Missing: true)))
                .ToList();

            tables.Add(new MaskingTableView(t.Schema, t.Name, tableRules.FirstOrDefault(r => r.IsTableRule)?.Action, columns, Missing: false));
        }

        // Rules for tables that are no longer there.
        foreach (var orphan in byTable.Values)
        {
            var first = orphan[0];
            tables.Add(new MaskingTableView(first.SchemaName, first.TableName,
                orphan.FirstOrDefault(r => r.IsTableRule)?.Action,
                orphan.Where(r => !r.IsTableRule).Select(r => new MaskingColumnView(r.ColumnName, "?", r.Action, null, Missing: true)).ToList(),
                Missing: true));
        }

        return tables.OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
    }
}
