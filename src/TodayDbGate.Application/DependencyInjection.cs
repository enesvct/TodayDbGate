using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodayDbGate.Application.Services;

namespace TodayDbGate.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, Action<GatewayOptions>? configure = null)
    {
        var options = new GatewayOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ConnectionService>();
        services.AddScoped<TokenService>();
        services.AddScoped<AccessPolicyService>();
        services.AddScoped<QueryExecutionService>();
        return services;
    }
}
