using System.Text;
using Microsoft.AspNetCore.Mvc;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;
using TodayDbGate.Web.Models;

namespace TodayDbGate.Web.Controllers;

public sealed class AuditController(IAuditLogReader audit, IAuditFeed feed) : Controller
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public async Task<IActionResult> Index([FromQuery] AuditFilter filter, CancellationToken ct)
    {
        var page = await audit.SearchAsync(filter, ct);
        var stats = await audit.GetStatsAsync(ct);
        return View(new AuditIndexModel(filter, page, stats));
    }

    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var detail = await audit.GetAsync(id, ct);
        if (detail is null) return NotFound();

        // Stored results can hold personal or business data; keep them out of browser and proxy caches.
        Response.Headers.CacheControl = "no-store";
        return View(AuditDetailsModel.From(detail));
    }

    /// <summary>The same details as an HTML fragment, for the slide-over panel on the overview page.</summary>
    public async Task<IActionResult> Panel(long id, CancellationToken ct)
    {
        var detail = await audit.GetAsync(id, ct);
        if (detail is null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        return PartialView("_DetailPanel", AuditDetailsModel.From(detail));
    }

    /// <summary>
    /// Server-sent events: one "entry" event per audit entry saved after <paramref name="after"/>.
    /// On reconnect the browser sends Last-Event-ID and missed entries are replayed from the database.
    /// </summary>
    public async Task Stream(long? after, [FromServices] IHostApplicationLifetime lifetime, CancellationToken requestAborted)
    {
        var lastId = after ?? 0;
        if (long.TryParse(Request.Headers["Last-Event-ID"], out var lastEventId))
            lastId = Math.Max(lastId, lastEventId);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no"; // don't let a reverse proxy buffer the stream

        // End the stream when the client leaves or the app shuts down (otherwise shutdown waits for it).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, lifetime.ApplicationStopping);
        var ct = cts.Token;

        // Subscribe before reading the backlog so nothing saved in between is missed; duplicates are skipped by id.
        using var subscription = feed.Subscribe();

        try
        {
            await WriteAsync("retry: 3000\n\n", ct);

            foreach (var item in await audit.GetAfterAsync(lastId, 200, ct))
                lastId = await SendAsync(item, lastId, ct);

            while (!ct.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeat.CancelAfter(HeartbeatInterval);
                try
                {
                    await subscription.Reader.WaitToReadAsync(heartbeat.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await WriteAsync(": ping\n\n", ct); // keeps idle connections and proxies alive
                    continue;
                }

                while (subscription.Reader.TryRead(out var item))
                    lastId = await SendAsync(item, lastId, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client went away or the app is stopping.
        }
    }

    private async Task<long> SendAsync(AuditLogItem item, long lastId, CancellationToken ct)
    {
        if (item.Id <= lastId) return lastId;
        await WriteAsync($"id: {item.Id}\nevent: entry\ndata: {LiveEntry.ToJson(LiveEntry.From(item))}\n\n", ct);
        return item.Id;
    }

    private async Task WriteAsync(string text, CancellationToken ct)
    {
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
        await Response.Body.FlushAsync(ct);
    }
}
