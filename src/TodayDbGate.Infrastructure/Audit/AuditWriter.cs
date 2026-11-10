using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TodayDbGate.Application.Models;

namespace TodayDbGate.Infrastructure.Audit;

/// <summary>
/// Background service: drains the audit queue in batches into audit.db and runs maintenance once an hour.
/// Encoding (capping, compression, encryption) happens here, off the request path.
/// </summary>
public sealed class AuditWriter(
    ChannelAuditSink sink,
    IDbContextFactory<AuditDbContext> dbFactory,
    AuditOutputCodec codec,
    AuditMaintenance maintenance,
    AuditFeed feed,
    AuditOptions options,
    TimeProvider time,
    ILogger<AuditWriter> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextMaintenance = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wake up for new events, or at least once a minute to check whether maintenance is due.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(TimeSpan.FromMinutes(1));
                await sink.Reader.WaitToReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            catch (OperationCanceledException) { break; }

            await DrainAsync(stoppingToken);

            if (time.GetUtcNow().UtcDateTime >= nextMaintenance)
            {
                try { await maintenance.RunAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Audit maintenance failed."); }
                nextMaintenance = time.GetUtcNow().UtcDateTime + MaintenanceInterval;
            }
        }

        // Shutting down: write what is still queued.
        await DrainAsync(CancellationToken.None);
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        var batch = new List<AuditEvent>(BatchSize);
        while (true)
        {
            batch.Clear();
            while (batch.Count < BatchSize && sink.Reader.TryRead(out var e)) batch.Add(e);
            if (batch.Count == 0) return;

            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var entries = batch.Select(ToEntry).ToList();
                db.AuditLog.AddRange(entries);
                await db.SaveChangesAsync(ct);

                // Ids exist now; tell live viewers.
                feed.Publish(entries.Select(AuditMapping.ToItem));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not write {Count} audit entries.", batch.Count);
            }
        }
    }

    private AuditLogEntry ToEntry(AuditEvent e)
    {
        var input = e.Input;
        var inputTruncated = input is not null && input.Length > options.MaxInputChars;
        if (inputTruncated) input = input![..options.MaxInputChars];

        EncodedOutput? output = null;
        if (options.StoreOutput && e.Output is not null)
        {
            try { output = codec.Encode(e.Output); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not encode audit output for {Tool}.", e.Tool); }
        }

        return new AuditLogEntry
        {
            Timestamp = e.Timestamp,
            TokenId = e.TokenId,
            TokenName = e.TokenName,
            Tool = e.Tool,
            Alias = e.Alias,
            Input = input,
            InputTruncated = inputTruncated,
            Status = e.Status.ToString(),
            Error = e.Error,
            DurationMs = e.DurationMs,
            RowCount = e.RowCount,
            Truncated = e.Truncated,
            RecordsAffected = e.RecordsAffected,
            Output = output?.Blob,
            OutputBytes = output?.JsonBytes,
            OutputTruncated = output?.Truncated ?? false
        };
    }
}
