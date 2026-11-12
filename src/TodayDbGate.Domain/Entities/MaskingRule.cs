using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Domain.Entities;

/// <summary>
/// A masking rule for one column, or for a whole table when <see cref="ColumnName"/> is empty.
/// Names are matched case-insensitively against the origin of each result column.
/// </summary>
public class MaskingRule
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }
    public DbConnectionEntry Connection { get; set; } = null!;

    /// <summary>Schema as listed by the database ("public", "dbo", the MySQL database name).</summary>
    public string SchemaName { get; set; } = "";

    public required string TableName { get; set; }

    /// <summary>Empty for a table-level rule.</summary>
    public string ColumnName { get; set; } = "";

    public MaskAction Action { get; set; }

    public bool IsTableRule => ColumnName.Length == 0;
}
