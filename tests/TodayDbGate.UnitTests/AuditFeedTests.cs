using TodayDbGate.Application.Models;
using TodayDbGate.Infrastructure.Audit;

namespace TodayDbGate.UnitTests;

public class AuditFeedTests
{
    private static AuditLogItem Item(long id) =>
        new(id, DateTime.UtcNow, "t", "execute_query", "db", "SELECT 1", AuditStatus.Success, 1, 1, true);

    [Fact]
    public void Every_subscriber_gets_every_item()
    {
        var feed = new AuditFeed();
        using var a = feed.Subscribe();
        using var b = feed.Subscribe();

        feed.Publish([Item(1), Item(2)]);

        Assert.Equal(2, a.Reader.Count);
        Assert.Equal(2, b.Reader.Count);
        Assert.True(a.Reader.TryRead(out var first));
        Assert.Equal(1, first.Id);
    }

    [Fact]
    public void Disposed_subscription_stops_receiving_and_completes()
    {
        var feed = new AuditFeed();
        var sub = feed.Subscribe();
        sub.Dispose();

        feed.Publish([Item(1)]);

        Assert.Equal(0, feed.SubscriberCount);
        Assert.True(sub.Reader.Completion.IsCompleted);
    }

    [Fact]
    public void Slow_subscriber_keeps_the_newest_items()
    {
        var feed = new AuditFeed();
        using var sub = feed.Subscribe();

        feed.Publish(Enumerable.Range(1, 600).Select(i => Item(i)));

        Assert.Equal(500, sub.Reader.Count);
        Assert.True(sub.Reader.TryRead(out var oldestKept));
        Assert.Equal(101, oldestKept.Id);
    }
}
