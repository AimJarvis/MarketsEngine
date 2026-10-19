using System.Net.Http.Json;
using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Strategies;

namespace MarketsEngine.Infrastructure.Strategies;

/// <summary>
/// Calls out to a separately-run Python service wrapping the Kronos foundation model
/// (https://github.com/shiyu-coder/Kronos) for price forecasting. Kronos itself only forecasts
/// future candles - it has no concept of options, direction-to-trade, or risk; this class is
/// the entire "turn a forecast into a buy signal" translation layer, and it is exactly as
/// unproven as any other ML price forecast: never trust it without a real backtest first.
/// </summary>
public class KronosStrategy(HttpClient httpClient, StrategySettings settings) : IStrategy
{
    public async Task<StrategySignal> EvaluateAsync(StrategyContext context, CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "Strategy:KronosServiceUrl is not configured. Set it in appsettings.json, e.g. \"http://localhost:8000\".");
        }

        if (context.RecentPrices.Count < 2)
        {
            return StrategySignal.None;
        }

        var request = new KronosForecastRequest(
            context.RecentPrices.Select(p => p.Price).ToArray(),
            settings.KronosForecastHorizon);

        var response = await httpClient.PostAsJsonAsync("/forecast", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var forecast = await response.Content.ReadFromJsonAsync<KronosForecastResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Kronos service returned an empty response.");

        if (forecast.ForecastPrices.Length == 0)
        {
            return StrategySignal.None;
        }

        var currentPrice = context.RecentPrices[^1].Price;
        var predictedPrice = forecast.ForecastPrices[^1];

        if (currentPrice <= 0)
        {
            return StrategySignal.None;
        }

        var predictedChangePercent = (predictedPrice - currentPrice) / currentPrice * 100m;

        return predictedChangePercent >= settings.KronosDirectionThresholdPercent
            ? new StrategySignal(true, $"{context.TradingSymbol} Kronos forecast +{predictedChangePercent:F2}% over next {settings.KronosForecastHorizon} candles")
            : StrategySignal.None;
    }

    private record KronosForecastRequest(decimal[] Prices, int Horizon);
    private record KronosForecastResponse(decimal[] ForecastPrices);
}
