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

        if (context.RecentCandles.Count < 2)
        {
            return StrategySignal.None;
        }

        // Send full OHLCV - Kronos is an OHLCV model and forecasts best with real intrabar range
        // and volume. In backtest these are real HistoricalBar candles; on the live quote-snapshot
        // path they are currently degenerate (O=H=L=C=LTP, V=0), which degrades the forecast until
        // real-time bar aggregation exists (see Candle / docs/TODO.md).
        var candles = context.RecentCandles;
        var request = new KronosForecastRequest(
            candles.Select(c => c.Open).ToArray(),
            candles.Select(c => c.High).ToArray(),
            candles.Select(c => c.Low).ToArray(),
            candles.Select(c => c.Close).ToArray(),
            candles.Select(c => c.Volume).ToArray(),
            // Real bar timestamps (unix seconds, UTC) so Kronos's time features reflect the true
            // cadence (daily vs. intraday) instead of the service guessing minute spacing.
            candles.Select(c => new DateTimeOffset(DateTime.SpecifyKind(c.TimestampUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()).ToArray(),
            settings.KronosForecastHorizon);

        var response = await httpClient.PostAsJsonAsync("/forecast", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var forecast = await response.Content.ReadFromJsonAsync<KronosForecastResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Kronos service returned an empty response.");

        if (forecast.ForecastPrices.Length == 0)
        {
            return StrategySignal.None;
        }

        var currentPrice = candles[^1].Close;
        var predictedPrice = forecast.ForecastPrices[^1];

        if (currentPrice <= 0)
        {
            return StrategySignal.None;
        }

        var predictedChangePercent = (predictedPrice - currentPrice) / currentPrice * 100m;

        if (predictedChangePercent < settings.KronosDirectionThresholdPercent)
        {
            return StrategySignal.None;
        }

        // Size the exit off the forecast's own magnitude rather than the global default - a
        // forecast predicting a big move earns a wider take-profit and (scaled down) a wider
        // stop; a barely-over-threshold forecast gets a tight leash on both.
        var takeProfitPercent = predictedChangePercent * settings.KronosTakeProfitFactor;
        var stopLossPercent = predictedChangePercent * settings.KronosStopLossFactor;

        return new StrategySignal(
            true,
            $"{context.TradingSymbol} Kronos forecast +{predictedChangePercent:F2}% over next {settings.KronosForecastHorizon} candles",
            StopLossPercent: stopLossPercent,
            TakeProfitPercent: takeProfitPercent);
    }

    private record KronosForecastRequest(
        decimal[] Open, decimal[] High, decimal[] Low, decimal[] Close, decimal[] Volume,
        long[] Timestamps, int Horizon);
    private record KronosForecastResponse(decimal[] ForecastPrices);
}
