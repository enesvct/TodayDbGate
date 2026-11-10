using Microsoft.EntityFrameworkCore;

namespace TodayDbGate.Infrastructure.Audit;

/// <summary>One stored MCP tool call.</summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int TokenId { get; set; }
    public required string TokenName { get; set; }
    public required string Tool { get; set; }
    public string? Alias { get; set; }
    public string? Input { get; set; }
    public bool InputTruncated { get; set; }
    public required string Status { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }
    public int? RowCount { get; set; }
    public bool? Truncated { get; set; }
    public int? RecordsAffected { get; set; }

    /// <summary>Result as JSON, Brotli-compressed, then encrypted with Data Protection.</summary>
    public byte[]? Output { get; set; }

    /// <summary>Size of the stored JSON before compression.</summary>
    public int? OutputBytes { get; set; }

    /// <summary>True when the stored output is shorter than what the client received.</summary>
    public bool OutputTruncated { get; set; }
}

/// <summary>
/// The audit log lives in its own SQLite file (audit.db) so it can grow, be trimmed and be vacuumed
/// without touching the gateway's configuration database.
/// </summary>
public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.Property(x => x.TokenName).HasMaxLength(100);
            e.Property(x => x.Tool).HasMaxLength(50);
            e.Property(x => x.Alias).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.TokenName);
            e.HasIndex(x => x.Alias);
        });
    }
}
