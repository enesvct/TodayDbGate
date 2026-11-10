using TodayDbGate.Domain.Entities;

namespace TodayDbGate.Application.Abstractions;

public interface IConnectionRepository
{
    Task<List<DbConnectionEntry>> ListAsync(CancellationToken ct);
    Task<DbConnectionEntry?> GetByIdAsync(int id, CancellationToken ct);
    Task<bool> AliasExistsAsync(string aliasName, int? excludeId, CancellationToken ct);
    Task<HashSet<int>> GetExistingIdsAsync(IEnumerable<int> ids, CancellationToken ct);
    Task AddAsync(DbConnectionEntry entry, CancellationToken ct);
    Task DeleteAsync(DbConnectionEntry entry, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface ITokenRepository
{
    /// <summary>All tokens with their grants and granted connections.</summary>
    Task<List<AccessToken>> ListAsync(CancellationToken ct);

    /// <summary>Tracked, with grants and connections loaded.</summary>
    Task<AccessToken?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Read-only lookup for authentication, with grants and connections loaded.</summary>
    Task<AccessToken?> GetByHashAsync(string tokenHash, CancellationToken ct);

    Task AddAsync(AccessToken token, CancellationToken ct);
    Task DeleteAsync(AccessToken token, CancellationToken ct);
    Task UpdateLastUsedAsync(int tokenId, DateTime usedAt, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
