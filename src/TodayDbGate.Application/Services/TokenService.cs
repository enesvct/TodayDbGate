using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Security;
using TodayDbGate.Domain.Entities;

namespace TodayDbGate.Application.Services;

/// <summary>Admin operations on access tokens and their grants.</summary>
public sealed class TokenService(
    ITokenRepository tokens,
    IConnectionRepository connections,
    ITokenHasher hasher,
    TimeProvider time)
{
    public async Task<IReadOnlyList<TokenSummary>> ListAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return (await tokens.ListAsync(ct)).Select(t => ToSummary(t, now)).ToList();
    }

    public async Task<TokenSummary?> GetAsync(int id, CancellationToken ct = default)
    {
        var token = await tokens.GetByIdAsync(id, ct);
        return token is null ? null : ToSummary(token, time.GetUtcNow().UtcDateTime);
    }

    /// <summary>Creates a token. The plain value in the result must be shown to the admin now; it is not stored.</summary>
    public async Task<OperationResult<CreatedToken>> CreateAsync(TokenInput input, CancellationToken ct = default)
    {
        var errors = await ValidateAsync(input, ct);
        if (errors.Count > 0) return OperationResult<CreatedToken>.Failure(errors);

        var plain = AccessTokenGenerator.Generate();
        var token = new AccessToken
        {
            Name = input.Name.Trim(),
            TokenHash = hasher.Hash(plain),
            TokenPrefix = AccessTokenGenerator.GetDisplayPrefix(plain),
            IsActive = true,
            CreatedAt = time.GetUtcNow().UtcDateTime,
            ExpiresAt = ToUtc(input.ExpiresAt),
            TokenConnections = DistinctGrants(input)
                .Select(g => new TokenConnection { ConnectionId = g.ConnectionId, AccessLevel = g.AccessLevel })
                .ToList()
        };

        await tokens.AddAsync(token, ct);
        return OperationResult<CreatedToken>.Success(new CreatedToken(token.Id, plain));
    }

    /// <summary>Updates name, expiry and grants. The token value itself does not change.</summary>
    public async Task<OperationResult> UpdateAsync(int id, TokenInput input, CancellationToken ct = default)
    {
        var token = await tokens.GetByIdAsync(id, ct);
        if (token is null) return OperationResult.Failure("Token not found.");

        var errors = await ValidateAsync(input, ct);
        if (errors.Count > 0) return OperationResult.Failure(errors);

        token.Name = input.Name.Trim();
        token.ExpiresAt = ToUtc(input.ExpiresAt);

        // Apply the grant changes as a diff so EF does not delete and re-insert the same key.
        var wanted = DistinctGrants(input).ToDictionary(g => g.ConnectionId, g => g.AccessLevel);
        foreach (var existing in token.TokenConnections.ToList())
        {
            if (wanted.Remove(existing.ConnectionId, out var level))
                existing.AccessLevel = level;
            else
                token.TokenConnections.Remove(existing);
        }
        foreach (var (connectionId, level) in wanted)
            token.TokenConnections.Add(new TokenConnection { ConnectionId = connectionId, AccessLevel = level });

        await tokens.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> SetActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        var token = await tokens.GetByIdAsync(id, ct);
        if (token is null) return OperationResult.Failure("Token not found.");

        token.IsActive = isActive;
        await tokens.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var token = await tokens.GetByIdAsync(id, ct);
        if (token is null) return OperationResult.Failure("Token not found.");

        await tokens.DeleteAsync(token, ct);
        return OperationResult.Success();
    }

    private async Task<List<string>> ValidateAsync(TokenInput input, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors.Add("Name is required.");
        else if (input.Name.Trim().Length > 100) errors.Add("Name must be at most 100 characters.");

        if (input.ExpiresAt is not null && ToUtc(input.ExpiresAt) <= time.GetUtcNow().UtcDateTime)
            errors.Add("Expiry date must be in the future.");

        var grants = DistinctGrants(input).ToList();
        if (grants.Any(g => !Enum.IsDefined(g.AccessLevel))) errors.Add("Unknown access level.");

        var ids = grants.Select(g => g.ConnectionId).ToList();
        var existing = await connections.GetExistingIdsAsync(ids, ct);
        if (ids.Any(id => !existing.Contains(id))) errors.Add("One or more selected connections no longer exist.");

        return errors;
    }

    /// <summary>One grant per connection; if a connection appears twice the last entry wins.</summary>
    private static IEnumerable<TokenGrantInput> DistinctGrants(TokenInput input) =>
        input.Grants.GroupBy(g => g.ConnectionId).Select(g => g.Last());

    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Unspecified } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        var v => v.Value.ToUniversalTime()
    };

    private static TokenSummary ToSummary(AccessToken t, DateTime now) => new(
        t.Id, t.Name, t.TokenPrefix, t.IsActive,
        IsExpired: t.ExpiresAt is not null && t.ExpiresAt <= now,
        t.CreatedAt, t.ExpiresAt, t.LastUsedAt,
        t.TokenConnections
            .OrderBy(tc => tc.Connection?.AliasName)
            .Select(tc => new TokenGrantSummary(tc.ConnectionId, tc.Connection?.AliasName ?? "?", tc.AccessLevel))
            .ToList());
}
