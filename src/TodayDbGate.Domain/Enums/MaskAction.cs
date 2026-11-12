namespace TodayDbGate.Domain.Enums;

/// <summary>What happens to values from a column (or a whole table) before an MCP client sees them.</summary>
public enum MaskAction
{
    /// <summary>Column only: never mask, not even by personal-data detection.</summary>
    Allow = 0,

    /// <summary>Keep a hint of the value: <c>a***@example.com</c>, <c>A*** V***</c>, <c>*******67</c>.</summary>
    Partial = 1,

    /// <summary>Replace with a stable alias (<c>email_3f9a1c2b7d</c>): equal values get equal aliases, so grouping and counting still work.</summary>
    Pseudonym = 2,

    /// <summary>Replace with <c>[REDACTED]</c>.</summary>
    Redact = 3,

    /// <summary>Table only: refuse any query whose result contains a column from this table.</summary>
    Block = 4
}
