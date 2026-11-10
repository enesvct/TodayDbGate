using System.Threading.Channels;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;

namespace TodayDbGate.Infrastructure.Audit;

/// <summary>
/// Queues audit events in memory for <see cref="AuditWriter"/>. Never blocks a request: if the queue is full
/// the event is dropped and counted (shown on the audit page), rather than slowing every MCP call down.
/// </summary>
public sealed class ChannelAuditSink : IAuditSink
{
    private readonly Channel<AuditEvent> _channel;
    private readonly bool _enabled;
    private long _dropped;

    public ChannelAuditSink(AuditOptions options)
    {
        _enabled = options.Enabled;
        // FullMode.Wait + TryWrite: TryWrite returns false instead of waiting when the queue is full,
        // which lets us count drops. (DropWrite would also report success for dropped items.)
        _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(Math.Max(1, options.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    }

    public ChannelReader<AuditEvent> Reader => _channel.Reader;

    public long DroppedEvents => Interlocked.Read(ref _dropped);

    public void Record(AuditEvent auditEvent)
    {
        if (!_enabled) return;
        if (!_channel.Writer.TryWrite(auditEvent))
            Interlocked.Increment(ref _dropped);
    }
}
