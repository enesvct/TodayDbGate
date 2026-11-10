using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;

namespace TodayDbGate.Infrastructure.Audit;

public sealed class AuditLogReader(
    IDbContextFactory<AuditDbContext> dbFactory,
    AuditOutputCodec codec,
    AuditOptions options,
    ChannelAuditSink sink) : IAuditLogReader
{
    private const int PreviewLength = AuditMapping.PreviewLength;

    public async Task<IReadOnlyList<AuditLogItem>> GetAfterAsync(long afterId, int max, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entries = await db.AuditLog.AsNoTracking()
            .Where(e => e.Id > afterId)
            .OrderBy(e => e.Id)
            .Take(Math.Clamp(max, 1, 500))
            .ToListAsync(ct);
        return entries.Select(AuditMapping.ToItem).ToList();
    }

    public async Task<AuditPage> SearchAsync(AuditFilter filter, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.AuditLog.AsNoTracking();

        // LIKE is case-insensitive for ASCII in SQLite; Contains() would translate to case-sensitive instr().
        if (!string.IsNullOrWhiteSpace(filter.Token))
            query = query.Where(e => EF.Functions.Like(e.TokenName, $"%{filter.Token.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filter.Alias))
            query = query.Where(e => e.Alias == filter.Alias.Trim().ToLower());
        if (filter.Status is { } status)
            query = query.Where(e => e.Status == status.ToString());
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(e => e.Input != null && EF.Functions.Like(e.Input, $"%{filter.Search.Trim()}%"));

        var pageSize = Math.Clamp(filter.PageSize, 10, 200);
        var total = await query.CountAsync(ct);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (total + pageSize - 1) / pageSize));

        var rows = await query
            .OrderByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id, e.Timestamp, e.TokenName, e.Tool, e.Alias,
                Preview = e.Input == null ? null : e.Input.Substring(0, PreviewLength),
                e.Status, e.DurationMs, e.RowCount,
                HasOutput = e.Output != null
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new AuditLogItem(
            r.Id, r.Timestamp, r.TokenName, r.Tool, r.Alias, r.Preview,
            Enum.Parse<AuditStatus>(r.Status), r.DurationMs, r.RowCount, r.HasOutput)).ToList();

        return new AuditPage(items, total, page, pageSize);
    }

    public async Task<AuditLogDetail?> GetAsync(long id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.AuditLog.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;

        string? output = null;
        if (e.Output is not null)
        {
            try { output = codec.Decode(e.Output); }
            catch (CryptographicException) { output = "(The stored output cannot be decrypted: the Data Protection keys have changed.)"; }
        }

        return new AuditLogDetail(
            e.Id, e.Timestamp, e.TokenId, e.TokenName, e.Tool, e.Alias, e.Input, e.InputTruncated,
            Enum.Parse<AuditStatus>(e.Status), e.Error, e.DurationMs, e.RowCount, e.Truncated, e.RecordsAffected,
            output, e.OutputBytes, e.OutputTruncated);
    }

    public async Task<AuditStats> GetStatsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await db.AuditLog.CountAsync(ct);
        var oldest = count == 0 ? (DateTime?)null : await db.AuditLog.MinAsync(e => e.Timestamp, ct);
        var bytes = await AuditMaintenance.GetFileBytesAsync(db, ct);

        return new AuditStats(bytes, count, oldest, options.RetentionDays, options.MaxDatabaseMb,
            sink.DroppedEvents, options.StoreOutput);
    }
}
