namespace TodayDbGate.Application.Models;

/// <summary>Plain connection settings. Only exists in memory; persisted as an encrypted connection string.</summary>
public sealed record ConnectionDetails(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate = false);

public sealed record QueryLimits(int MaxRows, int CommandTimeoutSeconds);

/// <summary>Result of a query. Cell values are JSON-friendly (primitives, dates, strings; binary as base64).</summary>
public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<object?[]> Rows,
    bool Truncated,
    int RecordsAffected,
    long ElapsedMs);

public sealed record ColumnInfo(string Name, string DataType, bool IsNullable);

public sealed record TableInfo(string Schema, string Name, IReadOnlyList<ColumnInfo> Columns);
