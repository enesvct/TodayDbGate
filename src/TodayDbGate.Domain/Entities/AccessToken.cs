namespace TodayDbGate.Domain.Entities;

/// <summary>
/// An access token given to an MCP client. Only an HMAC hash of the token is stored;
/// the plain value is shown to the admin once, at creation time.
/// </summary>
public class AccessToken
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string TokenHash { get; set; }

    /// <summary>First characters of the token, kept so admins can recognise it in the list.</summary>
    public required string TokenPrefix { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public ICollection<TokenConnection> TokenConnections { get; set; } = new List<TokenConnection>();

    public bool IsUsable(DateTime utcNow) => IsActive && (ExpiresAt is null || ExpiresAt > utcNow);
}
