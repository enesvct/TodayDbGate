using System.ComponentModel.DataAnnotations;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Web.Models;

public sealed class LoginModel
{
    public string? Username { get; set; } = "admin";
    [DataType(DataType.Password)] public string? Password { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class ChangePasswordModel
{
    [DataType(DataType.Password)] public string? CurrentPassword { get; set; }
    [DataType(DataType.Password)] public string? NewPassword { get; set; }
    [DataType(DataType.Password)] public string? ConfirmPassword { get; set; }
}

/// <param name="RecentJson">Latest audit entries (newest first) as JSON for the live feed's first render.</param>
public sealed record OverviewModel(int ConnectionCount, int TokenCount, int ActiveTokenCount, string McpUrl, string RecentJson);

public sealed class ConnectionFormModel
{
    public int? Id { get; set; }
    public ConnectionInput Input { get; set; } = new();
    public IReadOnlyList<string> Errors { get; set; } = [];
    public bool IsEdit => Id is not null;
}

public enum GrantLevel { None, ReadOnly, ReadWrite }

public sealed class GrantRow
{
    public int ConnectionId { get; set; }
    public string AliasName { get; set; } = "";
    public DatabaseType DatabaseType { get; set; }
    public GrantLevel Level { get; set; }
}

public sealed class TokenFormModel
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Last day the token works (UTC). Empty means it never expires.</summary>
    [DataType(DataType.Date)] public DateOnly? ExpiresOn { get; set; }

    public List<GrantRow> Rows { get; set; } = [];
    public IReadOnlyList<string> Errors { get; set; } = [];
    public bool IsEdit => Id is not null;

    public TokenInput ToInput() => new()
    {
        Name = Name,
        // Valid through the whole chosen day: expires at the next midnight UTC.
        ExpiresAt = ExpiresOn?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        Grants = Rows.Where(r => r.Level != GrantLevel.None)
            .Select(r => new TokenGrantInput
            {
                ConnectionId = r.ConnectionId,
                AccessLevel = r.Level == GrantLevel.ReadWrite ? AccessLevel.ReadWrite : AccessLevel.ReadOnly
            })
            .ToList()
    };
}

public sealed record CreatedTokenModel(string Name, string PlainToken, string McpUrl);

public static class Display
{
    public static string Utc(DateTime? value) => value is null ? "—" : value.Value.ToString("yyyy-MM-dd HH:mm") + " UTC";

    /// <summary>
    /// The dashboard stores "valid through day X" as an expiry at the following midnight UTC;
    /// show it back as day X. Any other expiry time is shown as is.
    /// </summary>
    public static string ValidThrough(DateTime? expiresAt) => expiresAt switch
    {
        null => "No expiry",
        { TimeOfDay.Ticks: 0 } e => e.AddDays(-1).ToString("yyyy-MM-dd") + " (UTC)",
        var e => "until " + Utc(e)
    };

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
    };

    public static string StatusBadge(AuditStatus status) => status switch
    {
        AuditStatus.Success => "badge-green",
        AuditStatus.Rejected => "badge-amber",
        AuditStatus.Denied => "badge-red",
        _ => "badge-red"
    };

    public static string DbType(DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => "PostgreSQL",
        DatabaseType.SqlServer => "SQL Server",
        DatabaseType.MySql => "MySQL",
        _ => type.ToString()
    };
}
