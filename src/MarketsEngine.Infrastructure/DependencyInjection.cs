using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Trading;
using MarketsEngine.Infrastructure.Brokers;
using MarketsEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketsEngine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Missing 'ConnectionStrings:Postgres'. Set it with: dotnet user-secrets set \"ConnectionStrings:Postgres\" \"...\"");

        services.AddDbContext<MarketsEngineDbContext>(options => options.UseNpgsql(connectionString));

        services.AddOptions<TradingSettings>()
            .Bind(configuration.GetSection(TradingSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IOrderExecutionService, OrderExecutionService>();
        services.AddSingleton(TimeProvider.System);

        services.Configure<KiteConnectSettings>(configuration.GetSection(KiteConnectSettings.SectionName));
        services.AddScoped<KiteSessionStore>();
        services.AddScoped<KiteClientFactory>();
        services.AddScoped<KiteInstrumentSyncService>();
        services.AddSingleton<KiteTickerService>();

        return services;
    }
}
