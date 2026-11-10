using TodayDbGate.Application.Models;

namespace TodayDbGate.Application.Abstractions;

/// <summary>Accepts audit events without blocking the caller. Writing happens in the background.</summary>
public interface IAuditSink
{
    void Record(AuditEvent auditEvent);
}

public interface IAuditLogReader
{
    Task<AuditPage> SearchAsync(AuditFilter filter, CancellationToken ct);

    /// <summary>Entries with an id greater than <paramref name="afterId"/>, oldest first.</summary>
    Task<IReadOnlyList<AuditLogItem>> GetAfterAsync(long afterId, int max, CancellationToken ct);
    Task<AuditLogDetail?> GetAsync(long id, CancellationToken ct);
    Task<AuditStats> GetStatsAsync(CancellationToken ct);
}
