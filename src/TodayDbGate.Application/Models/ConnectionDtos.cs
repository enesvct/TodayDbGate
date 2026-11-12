using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Models;

/// <summary>Create/edit form data for a connection. On edit, an empty password keeps the stored one.</summary>
public sealed class ConnectionInput
{
    public string AliasName { get; set; } = "";
    public DatabaseType DatabaseType { get; set; } = DatabaseType.PostgreSql;
    public string Host { get; set; } = "";
    public int? Port { get; set; }
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public bool TrustServerCertificate { get; set; }
    public string? Description { get; set; }
}

/// <summary>What the dashboard list shows. Never contains the password.</summary>
public sealed record ConnectionSummary(
    int Id,
    string AliasName,
    DatabaseType DatabaseType,
    string Host,
    int Port,
    string Database,
    string Username,
    string? Description,
    DateTime CreatedAt,
    int TokenCount,
    int MaskingRuleCount = 0,
    bool DetectPersonalData = true);

public sealed record ConnectionTestResult(bool Success, string Message, long ElapsedMs);
