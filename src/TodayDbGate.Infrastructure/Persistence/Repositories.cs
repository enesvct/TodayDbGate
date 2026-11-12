using Microsoft.EntityFrameworkCore;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Domain.Entities;

namespace TodayDbGate.Infrastructure.Persistence;

public sealed class ConnectionRepository(GatewayDbContext db) : IConnectionRepository
{
    public Task<List<DbConnectionEntry>> ListAsync(CancellationToken ct) =>
        db.Connections.AsNoTracking().Include(c => c.TokenConnections).Include(c => c.MaskingRules)
            .AsSplitQuery().OrderBy(c => c.AliasName).ToListAsync(ct);

    public Task<DbConnectionEntry?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Connections.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> AliasExistsAsync(string aliasName, int? excludeId, CancellationToken ct) =>
        db.Connections.AnyAsync(c => c.AliasName == aliasName && (excludeId == null || c.Id != excludeId), ct);

    public async Task<HashSet<int>> GetExistingIdsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return (await db.Connections.Where(c => list.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task AddAsync(DbConnectionEntry entry, CancellationToken ct)
    {
        db.Connections.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(DbConnectionEntry entry, CancellationToken ct)
    {
        db.Connections.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class TokenRepository(GatewayDbContext db) : ITokenRepository
{
    private IQueryable<AccessToken> WithGrants =>
        db.Tokens.Include(t => t.TokenConnections).ThenInclude(tc => tc.Connection);

    public Task<List<AccessToken>> ListAsync(CancellationToken ct) =>
        WithGrants.AsNoTracking().OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

    public Task<AccessToken?> GetByIdAsync(int id, CancellationToken ct) =>
        WithGrants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<AccessToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        WithGrants.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(AccessToken token, CancellationToken ct)
    {
        db.Tokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(AccessToken token, CancellationToken ct)
    {
        db.Tokens.Remove(token);
        await db.SaveChangesAsync(ct);
    }

    public Task UpdateLastUsedAsync(int tokenId, DateTime usedAt, CancellationToken ct) =>
        db.Tokens.Where(t => t.Id == tokenId).ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, usedAt), ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
