using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TodayDbGate.Infrastructure.Audit;

/// <summary>Keeps audit.db bounded: deletes by age, then by size, and gives freed pages back to the disk.</summary>
public sealed class AuditMaintenance(
    IDbContextFactory<AuditDbContext> dbFactory,
    AuditOptions options,
    TimeProvider time,
    ILogger<AuditMaintenance> logger)
{
    /// <returns>Number of entries deleted.</returns>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, options.RetentionDays));
        var deleted = await db.AuditLog.Where(e => e.Timestamp < cutoff).ExecuteDeleteAsync(ct);

        // Size cap: drop the oldest 10% at a time until the used space is under the limit.
        var maxBytes = (long)Math.Max(1, options.MaxDatabaseMb) * 1024 * 1024;
        for (var round = 0; round < 20 && await GetUsedBytesAsync(db, ct) > maxBytes; round++)
        {
            var count = await db.AuditLog.CountAsync(ct);
            if (count == 0) break;
            var lastId = await db.AuditLog.OrderBy(e => e.Id).Skip(Math.Max(1, count / 10) - 1).Select(e => e.Id).FirstAsync(ct);
            deleted += await db.AuditLog.Where(e => e.Id <= lastId).ExecuteDeleteAsync(ct);
        }

        if (deleted > 0)
        {
            // auto_vacuum=INCREMENTAL is set when the file is created, so this shrinks the file.
            await db.Database.ExecuteSqlRawAsync("PRAGMA incremental_vacuum;", ct);
            logger.LogInformation("Audit maintenance deleted {Count} entries.", deleted);
        }
        return deleted;
    }

    /// <summary>Bytes in pages that hold data (file size minus free pages).</summary>
    public static async Task<long> GetUsedBytesAsync(AuditDbContext db, CancellationToken ct)
    {
        var pages = await PragmaAsync(db, "page_count", ct);
        var free = await PragmaAsync(db, "freelist_count", ct);
        var size = await PragmaAsync(db, "page_size", ct);
        return (pages - free) * size;
    }

    /// <summary>Size of the database file (all pages, including free ones).</summary>
    public static async Task<long> GetFileBytesAsync(AuditDbContext db, CancellationToken ct) =>
        await PragmaAsync(db, "page_count", ct) * await PragmaAsync(db, "page_size", ct);

    private static async Task<long> PragmaAsync(AuditDbContext db, string name, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync(ct);
        await using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA {name};";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }
}
