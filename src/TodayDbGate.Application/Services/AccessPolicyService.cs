using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Security;

namespace TodayDbGate.Application.Services;

/// <summary>Turns a presented bearer token into a <see cref="TokenPrincipal"/>, or rejects it.</summary>
public sealed class AccessPolicyService(ITokenRepository tokens, ITokenHasher hasher, TimeProvider time)
{
    // Writing last_used_at on every request would be wasteful; once a minute is enough for the dashboard.
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    /// <returns>The principal, or null if the token is unknown, inactive or expired.</returns>
    public async Task<TokenPrincipal?> AuthenticateAsync(string? presentedToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(presentedToken)
            || presentedToken.Length > 200
            || !presentedToken.StartsWith(AccessTokenGenerator.Prefix, StringComparison.Ordinal))
            return null;

        var token = await tokens.GetByHashAsync(hasher.Hash(presentedToken), ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (token is null || !token.IsUsable(now)) return null;

        if (token.LastUsedAt is null || now - token.LastUsedAt.Value >= LastUsedResolution)
            await tokens.UpdateLastUsedAsync(token.Id, now, ct);

        var grants = token.TokenConnections
            .Select(tc => new TokenGrant(tc.ConnectionId, tc.Connection.AliasName, tc.Connection.DatabaseType, tc.AccessLevel))
            .OrderBy(g => g.AliasName)
            .ToList();

        return new TokenPrincipal(token.Id, token.Name, grants);
    }
}
