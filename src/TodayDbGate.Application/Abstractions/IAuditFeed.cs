using System.Threading.Channels;
using TodayDbGate.Application.Models;

namespace TodayDbGate.Application.Abstractions;

/// <summary>Live stream of audit entries as they are saved (for the dashboard's activity feed).</summary>
public interface IAuditFeed
{
    /// <summary>
    /// Starts receiving entries saved from now on. Subscribe first, then load anything older from
    /// <see cref="IAuditLogReader.GetAfterAsync"/>, so nothing falls between the two.
    /// </summary>
    IAuditSubscription Subscribe();
}

public interface IAuditSubscription : IDisposable
{
    ChannelReader<AuditLogItem> Reader { get; }
}
