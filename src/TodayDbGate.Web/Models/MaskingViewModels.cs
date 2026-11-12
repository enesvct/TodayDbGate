using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Web.Models;

public enum TableMaskMode { Normal, Redact, Block }

/// <summary>"Default" means no rule: personal-data detection (if on) decides.</summary>
public enum ColumnMaskMode { Default, Allow, Partial, Pseudonym, Redact }

public sealed class MaskingColumnForm
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public ColumnMaskMode Mode { get; set; }
    public ColumnMaskMode? Suggested { get; set; }
    public bool Missing { get; set; }
}

public sealed class MaskingTableForm
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public TableMaskMode Mode { get; set; }
    public bool Missing { get; set; }
    public List<MaskingColumnForm> Columns { get; set; } = [];

    public int RuleCount => (Mode == TableMaskMode.Normal ? 0 : 1) + Columns.Count(c => c.Mode != ColumnMaskMode.Default);
    public int PendingSuggestions => Columns.Count(c => c.Suggested is not null && c.Mode == ColumnMaskMode.Default);
}

public sealed class MaskingFormModel
{
    public int ConnectionId { get; set; }
    public string AliasName { get; set; } = "";
    public DatabaseType DatabaseType { get; set; }
    public string? SchemaError { get; set; }

    public bool DetectPersonalData { get; set; } = true;
    public List<MaskingTableForm> Tables { get; set; } = [];

    public static MaskingFormModel From(MaskingEditor editor) => new()
    {
        ConnectionId = editor.ConnectionId,
        AliasName = editor.AliasName,
        DatabaseType = editor.DatabaseType,
        SchemaError = editor.SchemaError,
        DetectPersonalData = editor.DetectPersonalData,
        Tables = editor.Tables.Select(t => new MaskingTableForm
        {
            Schema = t.Schema,
            Name = t.Name,
            Missing = t.Missing,
            Mode = t.TableAction switch
            {
                MaskAction.Redact => TableMaskMode.Redact,
                MaskAction.Block => TableMaskMode.Block,
                _ => TableMaskMode.Normal
            },
            Columns = t.Columns.Select(c => new MaskingColumnForm
            {
                Name = c.Name,
                DataType = c.DataType,
                Missing = c.Missing,
                Mode = ToMode(c.Action),
                Suggested = c.Suggested is null ? null : ToMode(c.Suggested)
            }).ToList()
        }).ToList()
    };

    public MaskingInput ToInput()
    {
        var input = new MaskingInput { DetectPersonalData = DetectPersonalData };
        foreach (var t in Tables)
        {
            if (t.Mode != TableMaskMode.Normal)
                input.Rules.Add(new MaskingRuleInput
                {
                    Schema = t.Schema, Table = t.Name,
                    Action = t.Mode == TableMaskMode.Block ? MaskAction.Block : MaskAction.Redact
                });

            foreach (var c in t.Columns.Where(c => c.Mode != ColumnMaskMode.Default))
                input.Rules.Add(new MaskingRuleInput
                {
                    Schema = t.Schema, Table = t.Name, Column = c.Name,
                    Action = c.Mode switch
                    {
                        ColumnMaskMode.Allow => MaskAction.Allow,
                        ColumnMaskMode.Partial => MaskAction.Partial,
                        ColumnMaskMode.Pseudonym => MaskAction.Pseudonym,
                        _ => MaskAction.Redact
                    }
                });
        }
        return input;
    }

    private static ColumnMaskMode ToMode(MaskAction? action) => action switch
    {
        MaskAction.Allow => ColumnMaskMode.Allow,
        MaskAction.Partial => ColumnMaskMode.Partial,
        MaskAction.Pseudonym => ColumnMaskMode.Pseudonym,
        MaskAction.Redact => ColumnMaskMode.Redact,
        _ => ColumnMaskMode.Default
    };

    public static string Label(ColumnMaskMode mode) => mode switch
    {
        ColumnMaskMode.Default => "Default",
        ColumnMaskMode.Allow => "Never mask",
        ColumnMaskMode.Partial => "Partial",
        ColumnMaskMode.Pseudonym => "Pseudonym",
        _ => "Redact"
    };
}
