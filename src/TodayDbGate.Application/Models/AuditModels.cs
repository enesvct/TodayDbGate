namespace TodayDbGate.Application.Models;

public enum AuditStatus
{
    /// <summary>The call ran and returned a result.</summary>
    Success,
    /// <summary>The token had no access to the alias.</summary>
    Denied,
    /// <summary>The query guard refused the SQL before it reached the database.</summary>
    Rejected,
    /// <summary>The database could not be reached or returned an error.</summary>
    Failed
}

/// <summary>
/// One MCP tool call, as handed to the audit sink. <see cref="Output"/> is the raw result
/// (e.g. a <see cref="QueryResult"/>); the writer caps, compresses and encrypts it off the request path.
/// </summary>
public sealed record AuditEvent(
    DateTime Timestamp,
    int TokenId,
    string TokenName,
    string Tool,
    string? Alias,
    string? Input,
    AuditStatus Status,
    string? Error,
    long DurationMs,
    object? Output = null,
    int? RowCount = null,
    bool? Truncated = null,
    int? RecordsAffected = null);

public sealed class AuditFilter
{
    public string? Token { get; set; }
    public string? Alias { get; set; }
    public AuditStatus? Status { get; set; }

    /// <summary>Text to look for in the input (SQL).</summary>
    public string? Search { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed record AuditLogItem(
    long Id,
    DateTime Timestamp,
    string TokenName,
    string Tool,
    string? Alias,
    string? InputPreview,
    AuditStatus Status,
    long DurationMs,
    int? RowCount,
    bool HasOutput);

public sealed record AuditPage(IReadOnlyList<AuditLogItem> Items, int Total, int Page, int PageSize)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

public sealed record AuditLogDetail(
    long Id,
    DateTime Timestamp,
    int TokenId,
    string TokenName,
    string Tool,
    string? Alias,
    string? Input,
    bool InputTruncated,
    AuditStatus Status,
    string? Error,
    long DurationMs,
    int? RowCount,
    bool? Truncated,
    int? RecordsAffected,
    string? OutputJson,
    int? OutputBytes,
    bool OutputTruncated);

public sealed record AuditStats(
    long DatabaseBytes,
    int EntryCount,
    DateTime? OldestEntry,
    int RetentionDays,
    int MaxDatabaseMb,
    long DroppedEvents,
    bool StoresOutput);
