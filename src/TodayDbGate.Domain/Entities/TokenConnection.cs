using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Domain.Entities;

/// <summary>Grants one token access to one connection, at a given access level.</summary>
public class TokenConnection
{
    public int TokenId { get; set; }
    public AccessToken Token { get; set; } = null!;

    public int ConnectionId { get; set; }
    public DbConnectionEntry Connection { get; set; } = null!;

    public AccessLevel AccessLevel { get; set; } = AccessLevel.ReadOnly;
}
