using System.Text.Json;
using TodayDbGate.Application.Models;

namespace TodayDbGate.Web.Models;

/// <summary>An audit entry as sent to the live activity feed (initial page data and server-sent events).</summary>
public sealed record LiveEntry(
    long Id, DateTime Timestamp, string Token, string Tool, string? Alias, string Status,
    int? Rows, long Ms, string? Input, bool HasOutput)
{
    public static LiveEntry From(AuditLogItem item) => new(
        item.Id,
        DateTime.SpecifyKind(item.Timestamp, DateTimeKind.Utc), // SQLite gives Unspecified; the browser converts to local time
        item.TokenName, item.Tool, item.Alias, item.Status.ToString(),
        item.RowCount, item.DurationMs, item.InputPreview, item.HasOutput);

    /// <summary>Default encoder escapes &lt; and &gt;, so the result is safe inside a &lt;script type="application/json"&gt; block.</summary>
    public static string ToJson(object value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}
