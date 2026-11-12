namespace TodayDbGate.Application.Models;

/// <summary>Plain connection settings. Only exists in memory; persisted as an encrypted connection string.</summary>
public sealed record ConnectionDetails(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate = false);

/// <param name="ResolveColumnOrigins">Ask the driver which table/column each result column comes from (needed for masking rules).</param>
public sealed record QueryLimits(int MaxRows, int CommandTimeoutSeconds, bool ResolveColumnOrigins = false);

/// <summary>Where a result column comes from, as reported by the database. Null parts are unknown.</summary>
public sealed record ColumnOrigin(string? Schema, string Table, string Column);

/// <summary>Result of a query. Cell values are JSON-friendly (primitives, dates, strings; binary as base64).</summary>
/// <param name="Origins">Per column, when requested and known; null for computed columns, unions and the like.</param>
/// <param name="MaskedColumns">Columns in which at least one value was masked.</param>
public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<object?[]> Rows,
    bool Truncated,
    int RecordsAffected,
    long ElapsedMs,
    IReadOnlyList<ColumnOrigin?>? Origins = null,
    IReadOnlyList<string>? MaskedColumns = null);

/// <param name="Masking">How values of this column are masked for the caller ("partial", "pseudonym", "redacted"), if at all.</param>
public sealed record ColumnInfo(string Name, string DataType, bool IsNullable, string? Masking = null);

public sealed record TableInfo(string Schema, string Name, IReadOnlyList<ColumnInfo> Columns);
