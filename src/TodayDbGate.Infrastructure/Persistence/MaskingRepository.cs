using Microsoft.EntityFrameworkCore;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Masking;
using TodayDbGate.Domain.Entities;

namespace TodayDbGate.Infrastructure.Persistence;

public sealed class MaskingRepository(GatewayDbContext db) : IMaskingRepository
{
    public async Task<MaskingPolicy> GetPolicyAsync(int connectionId, CancellationToken ct)
    {
        var detect = await db.Connections.Where(c => c.Id == connectionId).Select(c => (bool?)c.DetectPersonalData).FirstOrDefaultAsync(ct);
        if (detect is null) return MaskingPolicy.None;
        return new MaskingPolicy(detect.Value, await GetRulesAsync(connectionId, ct));
    }

    public Task<List<MaskingRule>> GetRulesAsync(int connectionId, CancellationToken ct) =>
        db.MaskingRules.AsNoTracking().Where(r => r.ConnectionId == connectionId)
            .OrderBy(r => r.SchemaName).ThenBy(r => r.TableName).ThenBy(r => r.ColumnName)
            .ToListAsync(ct);

    public async Task SaveAsync(int connectionId, bool detectPersonalData, IReadOnlyList<MaskingRule> rules, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.MaskingRules.Where(r => r.ConnectionId == connectionId).ExecuteDeleteAsync(ct);

        // Through the change tracker (not ExecuteUpdate) so an already loaded entry does not keep a stale value.
        var entry = await db.Connections.FirstAsync(c => c.Id == connectionId, ct);
        entry.DetectPersonalData = detectPersonalData;

        db.MaskingRules.AddRange(rules);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
