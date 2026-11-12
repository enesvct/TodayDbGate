using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Models;

/// <summary>Everything the masking page needs: the live schema merged with the saved rules.</summary>
/// <param name="SchemaError">Set when the schema could not be read; the saved rules are still shown.</param>
public sealed record MaskingEditor(
    int ConnectionId,
    string AliasName,
    DatabaseType DatabaseType,
    bool DetectPersonalData,
    IReadOnlyList<MaskingTableView> Tables,
    string? SchemaError);

/// <param name="Missing">A rule exists for this table but the table is no longer in the schema.</param>
public sealed record MaskingTableView(
    string Schema,
    string Name,
    MaskAction? TableAction,
    IReadOnlyList<MaskingColumnView> Columns,
    bool Missing);

/// <param name="Suggested">What the column name suggests (e.g. Partial for "email"), shown as a hint, never applied on its own.</param>
public sealed record MaskingColumnView(
    string Name,
    string DataType,
    MaskAction? Action,
    MaskAction? Suggested,
    bool Missing);

public sealed class MaskingRuleInput
{
    public string Schema { get; set; } = "";
    public string Table { get; set; } = "";

    /// <summary>Empty for a table-level rule.</summary>
    public string Column { get; set; } = "";

    public MaskAction Action { get; set; }
}

public sealed class MaskingInput
{
    public bool DetectPersonalData { get; set; } = true;
    public List<MaskingRuleInput> Rules { get; set; } = [];
}
