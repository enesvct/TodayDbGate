using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Infrastructure.Adapters;
using TodayDbGate.Infrastructure.Audit;
using TodayDbGate.Infrastructure.Persistence;
using TodayDbGate.Infrastructure.Security;

namespace TodayDbGate.Infrastructure;

public static class DependencyInjection
{
    /// <param name="contentRootPath">Base path used when Storage:DataDirectory is relative.</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        var dataDir = Path.GetFullPath(Path.Combine(contentRootPath, configuration["Storage:DataDirectory"] ?? "App_Data"));
        Directory.CreateDirectory(dataDir);
        services.AddSingleton(new StoragePaths(dataDir));

        // Keys must be persisted, otherwise a restart makes stored connection strings unreadable.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("TodayDbGate")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
        if (OperatingSystem.IsWindows())
            dataProtection.ProtectKeysWithDpapi(); // encrypt keys at rest, bound to the current Windows user

        services.AddDbContext<GatewayDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(dataDir, "gateway.db")}"));

        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton(sp => new TokenHashKeyProvider(
            sp.GetRequiredService<IDataProtectionProvider>(), Path.Combine(dataDir, "token-hash.key")));
        services.AddSingleton<ITokenHasher>(sp =>
            new HmacTokenHasher(sp.GetRequiredService<TokenHashKeyProvider>().GetKey()));

        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<ITokenRepository, TokenRepository>();

        services.AddSingleton<IDbAdapter, PostgresAdapter>();
        services.AddSingleton<IDbAdapter, SqlServerAdapter>();
        services.AddSingleton<IDbAdapter, MySqlAdapter>();
        services.AddSingleton<IDbAdapterFactory, DbAdapterFactory>();

        // Audit log: separate SQLite file, written in the background.
        var auditOptions = new AuditOptions();
        configuration.GetSection("Audit").Bind(auditOptions);
        services.AddSingleton(auditOptions);
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AuditDbContext>(o =>
            o.UseSqlite($"Data Source={Path.Combine(dataDir, "audit.db")}"));
        services.AddSingleton<ChannelAuditSink>();
        services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<ChannelAuditSink>());
        services.AddSingleton<AuditOutputCodec>();
        services.AddSingleton<AuditMaintenance>();
        services.AddSingleton<IAuditLogReader, AuditLogReader>();
        services.AddSingleton<AuditFeed>();
        services.AddSingleton<IAuditFeed>(sp => sp.GetRequiredService<AuditFeed>());
        services.AddHostedService<AuditWriter>();

        return services;
    }
}
