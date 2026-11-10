using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodayDbGate.Infrastructure.Audit;
using TodayDbGate.Infrastructure.Persistence;

namespace TodayDbGate.Infrastructure;

public static class DatabaseInitializer
{
    /// <summary>Applies migrations to gateway.db and audit.db. Call once at startup.</summary>
    public static void InitializeDatabases(this IServiceProvider services)
    {
        using (var scope = services.CreateScope())
            scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database.Migrate();

        using var audit = services.GetRequiredService<IDbContextFactory<AuditDbContext>>().CreateDbContext();
        audit.Database.OpenConnection();
        try
        {
            // Must be set before the first table exists (or followed by VACUUM) to let deletes shrink the file.
            using var cmd = audit.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA auto_vacuum;";
            var autoVacuum = Convert.ToInt64(cmd.ExecuteScalar());
            if (autoVacuum != 2) // 2 = INCREMENTAL
            {
                audit.Database.ExecuteSqlRaw("PRAGMA auto_vacuum = INCREMENTAL;");
                audit.Database.ExecuteSqlRaw("VACUUM;");
            }
            audit.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
            audit.Database.Migrate();
        }
        finally
        {
            audit.Database.CloseConnection();
        }
    }
}
