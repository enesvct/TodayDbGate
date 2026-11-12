using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Entities;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Masking;

/// <summary>The masking rules of one connection, ready for fast lookup by column origin.</summary>
public sealed class MaskingPolicy
{
    public static readonly MaskingPolicy None = new(false, []);

    private readonly Dictionary<string, List<MaskingRule>> _tableRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Table, string Column), List<MaskingRule>> _columnRules = new(new TableColumnComparer());

    public MaskingPolicy(bool detectPersonalData, IEnumerable<MaskingRule> rules)
    {
        DetectPersonalData = detectPersonalData;
        foreach (var rule in rules)
        {
            if (rule.IsTableRule)
                Add(_tableRules, rule.TableName, rule);
            else
                Add(_columnRules, (rule.TableName, rule.ColumnName), rule);
        }
    }

    public bool DetectPersonalData { get; }

    /// <summary>True when column origins are needed to apply this policy.</summary>
    public bool HasRules => _tableRules.Count > 0 || _columnRules.Count > 0;

    public bool IsEmpty => !DetectPersonalData && !HasRules;

    /// <summary>
    /// The action for a column, or null when no rule applies (then personal-data detection decides).
    /// A blocked table wins over everything; a column rule wins over a table-wide redaction.
    /// </summary>
    public MaskAction? Resolve(ColumnOrigin? origin) =>
        origin is null ? null : Resolve(origin.Schema, origin.Table, origin.Column);

    public MaskAction? Resolve(string? schema, string table, string column)
    {
        var tableRule = Find(_tableRules.GetValueOrDefault(table), schema);
        if (tableRule?.Action == MaskAction.Block) return MaskAction.Block;

        var columnRule = Find(_columnRules.GetValueOrDefault((table, column)), schema);
        return columnRule?.Action ?? tableRule?.Action;
    }

    public bool IsBlocked(string? schema, string table) =>
        Find(_tableRules.GetValueOrDefault(table), schema)?.Action == MaskAction.Block;

    // When the driver does not report a schema (SQL Server), any schema matches: masking too much beats too little.
    private static MaskingRule? Find(List<MaskingRule>? candidates, string? schema)
    {
        if (candidates is null) return null;
        if (string.IsNullOrEmpty(schema)) return candidates[0];
        return candidates.FirstOrDefault(r => string.IsNullOrEmpty(r.SchemaName) || string.Equals(r.SchemaName, schema, StringComparison.OrdinalIgnoreCase));
    }

    private static void Add<TKey>(Dictionary<TKey, List<MaskingRule>> map, TKey key, MaskingRule rule) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(rule);
    }

    private sealed class TableColumnComparer : IEqualityComparer<(string Table, string Column)>
    {
        public bool Equals((string Table, string Column) x, (string Table, string Column) y) =>
            string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Table, string Column) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
    }
}
