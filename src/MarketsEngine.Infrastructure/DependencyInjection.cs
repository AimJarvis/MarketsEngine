using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.LiveTrading;
using MarketsEngine.Domain.Notifications;
using MarketsEngine.Domain.Strategies;
using MarketsEngine.Domain.Trading;
using MarketsEngine.Infrastructure.Brokers;
using MarketsEngine.Infrastructure.Notifications;
using MarketsEngine.Infrastructure.Services;
using MarketsEngine.Infrastructure.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        services.AddOptions<LiveTradingSettings>()
            .Bind(configuration.GetSection(LiveTradingSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ILiveOrderExecutionService, LiveOrderExecutionService>();
        services.AddScoped<ITradingOrderGateway, TradingOrderGateway>();

        services.AddSingleton<LiveOrderReconciliationService>();
        services.AddHostedService(sp => sp.GetRequiredService<LiveOrderReconciliationService>());

        services.AddOptions<StrategySettings>()
            .Bind(configuration.GetSection(StrategySettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddKeyedSingleton<IStrategy>("SimpleMomentum", (sp, _) =>
        {
            var settings = sp.GetRequiredService<IOptions<StrategySettings>>().Value;
            return new SimpleMomentumStrategy(settings.MomentumThresholdPercent);
        });

        services.AddHttpClient("KronosService", (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<StrategySettings>>().Value;
            if (!string.IsNullOrWhiteSpace(settings.KronosServiceUrl))
            {
                client.BaseAddress = new Uri(settings.KronosServiceUrl);
            }
        });

        services.AddKeyedSingleton<IStrategy>("Kronos", (sp, _) =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("KronosService");
            var settings = sp.GetRequiredService<IOptions<StrategySettings>>().Value;
            return new KronosStrategy(httpClient, settings);
        });

        services.AddSingleton<StrategyRunnerService>();
        services.AddHostedService(sp => sp.GetRequiredService<StrategyRunnerService>());

        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddScoped<IWhatsAppNotifier, TwilioWhatsAppNotifier>();
        services.AddScoped<AccountPnLService>();

        services.AddOptions<PnLAlertSettings>()
            .Bind(configuration.GetSection(PnLAlertSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<PnLAlertService>();
        services.AddHostedService(sp => sp.GetRequiredService<PnLAlertService>());

        return services;
    }
}
