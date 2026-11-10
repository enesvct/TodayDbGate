using System.Collections.Concurrent;
using System.Threading.Channels;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;

namespace TodayDbGate.Infrastructure.Audit;

/// <summary>
/// In-memory fan-out of saved audit entries to live dashboard viewers. Each viewer has its own small
/// buffer; a viewer that stops reading only loses its own oldest items and never slows the writer.
/// </summary>
public sealed class AuditFeed : IAuditFeed
{
    private const int BufferPerSubscriber = 500;
    private readonly ConcurrentDictionary<Guid, Channel<AuditLogItem>> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public IAuditSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<AuditLogItem>(new BoundedChannelOptions(BufferPerSubscriber)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        _subscribers[id] = channel;
        return new Subscription(this, id, channel);
    }

    public void Publish(IEnumerable<AuditLogItem> items)
    {
        if (_subscribers.IsEmpty) return;
        foreach (var item in items)
            foreach (var channel in _subscribers.Values)
                channel.Writer.TryWrite(item);
    }

    private sealed class Subscription(AuditFeed feed, Guid id, Channel<AuditLogItem> channel) : IAuditSubscription
    {
        public ChannelReader<AuditLogItem> Reader => channel.Reader;

        public void Dispose()
        {
            if (feed._subscribers.TryRemove(id, out _))
                channel.Writer.TryComplete();
        }
    }
}
