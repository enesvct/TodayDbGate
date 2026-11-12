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

    /// <summary>
    /// Look for personal data (e-mail, phone, Turkish ID, IBAN, card numbers) in values that no rule covers,
    /// and mask what is found.
    /// </summary>
    public bool DetectPersonalData { get; set; } = true;

    public ICollection<TokenConnection> TokenConnections { get; set; } = new List<TokenConnection>();
    public ICollection<MaskingRule> MaskingRules { get; set; } = new List<MaskingRule>();
}
