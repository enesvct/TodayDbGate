using System.Text;
using Microsoft.EntityFrameworkCore;
using TodayDbGate.Domain.Entities;

namespace TodayDbGate.Infrastructure.Persistence;

/// <summary>The gateway's own metadata database (SQLite).</summary>
public class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<DbConnectionEntry> Connections => Set<DbConnectionEntry>();
    public DbSet<AccessToken> Tokens => Set<AccessToken>();
    public DbSet<TokenConnection> TokenConnections => Set<TokenConnection>();
    public DbSet<MaskingRule> MaskingRules => Set<MaskingRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbConnectionEntry>(e =>
        {
            e.ToTable("Connections");
            e.Property(x => x.AliasName).HasMaxLength(100);
            e.HasIndex(x => x.AliasName).IsUnique();
            e.Property(x => x.DatabaseType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(500);
            // No HasDefaultValue(true) here: EF would then treat an explicit false as "not set" and store true.
            // Existing rows get true from the migration instead.
        });

        modelBuilder.Entity<MaskingRule>(e =>
        {
            e.ToTable("MaskingRules");
            e.Property(x => x.SchemaName).HasMaxLength(128);
            e.Property(x => x.TableName).HasMaxLength(128);
            e.Property(x => x.ColumnName).HasMaxLength(128);
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            e.Ignore(x => x.IsTableRule);
            e.HasIndex(x => new { x.ConnectionId, x.SchemaName, x.TableName, x.ColumnName }).IsUnique();
            e.HasOne(x => x.Connection).WithMany(c => c.MaskingRules)
                .HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccessToken>(e =>
        {
            e.ToTable("Tokens");
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenPrefix).HasMaxLength(20);
        });

        modelBuilder.Entity<TokenConnection>(e =>
        {
            e.ToTable("TokenConnections");
            e.HasKey(x => new { x.TokenId, x.ConnectionId });
            e.Property(x => x.AccessLevel).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Token).WithMany(t => t.TokenConnections)
                .HasForeignKey(x => x.TokenId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Connection).WithMany(c => c.TokenConnections)
                .HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        // snake_case column names (alias_name, token_hash, ...) as in the schema draft.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
