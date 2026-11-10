using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Domain.Entities;

/// <summary>
/// A registered target database. The LLM only ever sees <see cref="AliasName"/>;
/// the real connection string is stored encrypted and never leaves the server.
/// </summary>
public class DbConnectionEntry
{
    public int Id { get; set; }
    public required string AliasName { get; set; }
    public DatabaseType DatabaseType { get; set; }
    public required string ConnectionStringEncrypted { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<TokenConnection> TokenConnections { get; set; } = new List<TokenConnection>();
}
