using TodayDbGate.Application.Models;

namespace TodayDbGate.Infrastructure.Audit;

internal static class AuditMapping
{
    public const int PreviewLength = 160;

    public static AuditLogItem ToItem(AuditLogEntry e) => new(
        e.Id, e.Timestamp, e.TokenName, e.Tool, e.Alias,
        e.Input is null ? null : e.Input.Length <= PreviewLength ? e.Input : e.Input[..PreviewLength],
        Enum.Parse<AuditStatus>(e.Status), e.DurationMs, e.RowCount, e.Output is not null);
}
