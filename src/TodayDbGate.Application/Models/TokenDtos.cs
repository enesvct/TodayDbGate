using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Models;

public sealed class TokenGrantInput
{
    public int ConnectionId { get; set; }
    public AccessLevel AccessLevel { get; set; } = AccessLevel.ReadOnly;
}

public sealed class TokenInput
{
    public string Name { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public bool CanSeeUnmasked { get; set; }
    public List<TokenGrantInput> Grants { get; set; } = [];
}

public sealed record TokenGrantSummary(int ConnectionId, string AliasName, AccessLevel AccessLevel);

public sealed record TokenSummary(
    int Id,
    string Name,
    string TokenPrefix,
    bool IsActive,
    bool IsExpired,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    IReadOnlyList<TokenGrantSummary> Grants,
    bool CanSeeUnmasked = false);

/// <summary>Returned once at creation. <see cref="PlainToken"/> is never stored and cannot be shown again.</summary>
public sealed record CreatedToken(int Id, string PlainToken);

/// <summary>One connection an authenticated token may use.</summary>
public sealed record TokenGrant(int ConnectionId, string AliasName, DatabaseType DatabaseType, AccessLevel AccessLevel);

/// <summary>An authenticated MCP caller.</summary>
/// <param name="CanSeeUnmasked">Masking rules and personal-data detection are skipped for this caller.</param>
public sealed record TokenPrincipal(int TokenId, string Name, IReadOnlyList<TokenGrant> Grants, bool CanSeeUnmasked = false)
{
    public TokenGrant? FindGrant(string? alias) =>
        alias is null
            ? null
            : Grants.FirstOrDefault(g => string.Equals(g.AliasName, alias.Trim(), StringComparison.OrdinalIgnoreCase));
}
