using KiteConnect;
using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.LiveTrading;
using MarketsEngine.Infrastructure.Brokers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainInstrument = MarketsEngine.Domain.Entities.Instrument;

namespace MarketsEngine.Infrastructure.Services;

/// <summary>
/// Real-money order execution via Kite Connect. Mirrors OrderExecutionService's validation
/// order and idempotency pattern, but against its own LiveOrder table and with two extra,
/// live-only guards: the instrument must be Kite-sourced (never place a real order against a
/// fictitious mock trading symbol), and nothing reaches Kite until ApproveAsync is called.
/// </summary>
public class LiveOrderExecutionService(
    MarketsEngineDbContext dbContext,
    KiteClientFactory kiteClientFactory,
    IOptionsMonitor<LiveTradingSettings> liveSettingsMonitor,
    IOptions<TradingSettings> tradingSettingsOptions,
    TimeProvider timeProvider,
    ILogger<LiveOrderExecutionService> logger) : ILiveOrderExecutionService
{
    private LiveTradingSettings LiveSettings => liveSettingsMonitor.CurrentValue;
    private readonly TradingSettings _tradingSettings = tradingSettingsOptions.Value;

    public async Task<LiveOrderSubmissionResult> SubmitOrderAsync(SubmitLiveOrderRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.LiveOrders
            .Include(o => o.Fill)
            .FirstOrDefaultAsync(o => o.IdempotencyKey == request.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            return existing.Status is LiveOrderStatus.Rejected or LiveOrderStatus.Failed
                ? LiveOrderSubmissionResult.Rejected("DuplicateIdempotencyKey", existing.RejectionReason ?? "Order with this idempotency key was already rejected.")
                : new LiveOrderSubmissionResult(true, existing.Status == LiveOrderStatus.PendingApproval, null, null, existing);
        }

        if (!LiveSettings.Enabled)
            return LiveOrderSubmissionResult.Rejected("LiveTradingDisabled", "Live trading is currently disabled by the kill switch.");

        if (request.Quantity <= 0)
            return LiveOrderSubmissionResult.Rejected("InvalidQuantity", "Quantity must be greater than zero.");

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var instrument = await dbContext.Instruments.FirstOrDefaultAsync(i => i.Id == request.InstrumentId, cancellationToken);
        if (instrument is null)
            return LiveOrderSubmissionResult.Rejected("InstrumentNotFound", "The specified instrument does not exist.");

        if (instrument.DataSource != "Kite")
        {
            return await RejectAsync(
                request, now, "NonKiteInstrument",
                $"Instrument '{instrument.TradingSymbol}' is not sourced from Kite (DataSource={instrument.DataSource}) - refusing to place a real order against a mock/fictitious symbol.",
                cancellationToken);
        }

        if (instrument.ExpiryDate is { } expiryDate && expiryDate < DateOnly.FromDateTime(now))
            return await RejectAsync(request, now, "InstrumentExpired", "This instrument has expired and can no longer be traded.", cancellationToken);

        var latestQuote = await dbContext.MarketQuotes
            .Where(q => q.InstrumentId == request.InstrumentId)
            .OrderByDescending(q => q.QuotedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestQuote is null)
            return await RejectAsync(request, now, "NoQuoteAvailable", "No quote is available for this instrument.", cancellationToken);

        var quoteAgeSeconds = (now - latestQuote.QuotedAtUtc).TotalSeconds;
        if (quoteAgeSeconds > _tradingSettings.QuoteStalenessSeconds)
        {
            return await RejectAsync(
                request, now, "StaleQuote",
                $"Quote is {quoteAgeSeconds:F0}s old, exceeding the {_tradingSettings.QuoteStalenessSeconds}s staleness limit.",
                cancellationToken);
        }

        var estimatedPrice = request.Side == OrderSide.Buy ? latestQuote.AskPrice : latestQuote.BidPrice;
        var notionalToday = await GetNotionalTradedTodayAsync(now, cancellationToken);

        var risk = LiveOrderRiskEvaluator.Evaluate(request.Quantity, estimatedPrice, instrument.LotSize, notionalToday, LiveSettings);
        if (risk.Result == LiveOrderRiskEvaluator.RiskResult.Rejected)
            return await RejectAsync(request, now, risk.ErrorCode!, risk.ErrorMessage!, cancellationToken);

        var order = new LiveOrder
        {
            Id = Guid.NewGuid(),
            InstrumentId = request.InstrumentId,
            Side = request.Side,
            Quantity = request.Quantity,
            IdempotencyKey = request.IdempotencyKey,
            Source = request.Source,
            RequestedAtUtc = now,
            Status = LiveSettings.RequireManualApproval ? LiveOrderStatus.PendingApproval : LiveOrderStatus.Approved
        };

        dbContext.LiveOrders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (order.Status == LiveOrderStatus.PendingApproval)
        {
            logger.LogInformation(
                "Live order {OrderId} parked PendingApproval: {Side} {Quantity} of {Symbol} ({Source})",
                order.Id, order.Side, order.Quantity, instrument.TradingSymbol, order.Source);
            return LiveOrderSubmissionResult.Pending(order);
        }

        return await PlaceWithKiteAsync(order, instrument, now, cancellationToken);
    }

    public async Task<LiveOrderSubmissionResult> ApproveAsync(Guid liveOrderId, CancellationToken cancellationToken = default)
    {
        var order = await dbContext.LiveOrders
            .Include(o => o.Instrument)
            .FirstOrDefaultAsync(o => o.Id == liveOrderId, cancellationToken);

        if (order is null)
            return LiveOrderSubmissionResult.Rejected("OrderNotFound", "The specified live order does not exist.");

        if (order.Status != LiveOrderStatus.PendingApproval)
            return LiveOrderSubmissionResult.Rejected("InvalidState", $"Order is in status {order.Status}, not PendingApproval.");

        if (!LiveSettings.Enabled)
        {
            return await RejectExistingAsync(order, "LiveTradingDisabled", "Live trading was disabled while this order was pending approval.", cancellationToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var latestQuote = await dbContext.MarketQuotes
            .Where(q => q.InstrumentId == order.InstrumentId)
            .OrderByDescending(q => q.QuotedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestQuote is null)
            return await RejectExistingAsync(order, "NoQuoteAvailable", "No quote is available for this instrument at approval time.", cancellationToken);

        var estimatedPrice = order.Side == OrderSide.Buy ? latestQuote.AskPrice : latestQuote.BidPrice;
        var notionalToday = await GetNotionalTradedTodayAsync(now, cancellationToken);
        var risk = LiveOrderRiskEvaluator.Evaluate(order.Quantity, estimatedPrice, order.Instrument!.LotSize, notionalToday, LiveSettings);

        if (risk.Result == LiveOrderRiskEvaluator.RiskResult.Rejected)
            return await RejectExistingAsync(order, risk.ErrorCode!, risk.ErrorMessage!, cancellationToken);

        order.Status = LiveOrderStatus.Approved;
        order.ApprovedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await PlaceWithKiteAsync(order, order.Instrument!, now, cancellationToken);
    }

    public async Task<LiveOrderSubmissionResult> RejectAsync(Guid liveOrderId, string reason, CancellationToken cancellationToken = default)
    {
        var order = await dbContext.LiveOrders.FirstOrDefaultAsync(o => o.Id == liveOrderId, cancellationToken);
        if (order is null)
            return LiveOrderSubmissionResult.Rejected("OrderNotFound", "The specified live order does not exist.");

        if (order.Status != LiveOrderStatus.PendingApproval)
            return LiveOrderSubmissionResult.Rejected("InvalidState", $"Order is in status {order.Status}, not PendingApproval.");

        return await RejectExistingAsync(order, "ManuallyRejected", reason, cancellationToken);
    }

    private async Task<LiveOrderSubmissionResult> PlaceWithKiteAsync(LiveOrder order, DomainInstrument instrument, DateTime now, CancellationToken cancellationToken)
    {
        var exchange = instrument.Kind == InstrumentKind.Equity ? Constants.Exchange.NSE : Constants.Exchange.NFO;
        var transactionType = order.Side == OrderSide.Buy ? Constants.Transaction.Buy : Constants.Transaction.Sell;

        try
        {
            var kite = await kiteClientFactory.CreateAuthenticatedClientAsync(cancellationToken);

            var margins = kite.GetMargins(Constants.Margin.Equity);
            var latestQuote = await dbContext.MarketQuotes
                .Where(q => q.InstrumentId == instrument.Id)
                .OrderByDescending(q => q.QuotedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var estimatedPrice = latestQuote?.AskPrice ?? 0m;
            var estimatedNotional = estimatedPrice * instrument.LotSize * order.Quantity;

            if (order.Side == OrderSide.Buy && margins.Available.Cash < estimatedNotional)
            {
                return await RejectExistingAsync(
                    order, "InsufficientMargin",
                    $"Available Kite cash margin {margins.Available.Cash:F2} is less than the estimated order value {estimatedNotional:F2}.",
                    cancellationToken);
            }

            var response = kite.PlaceOrder(
                Exchange: exchange,
                TradingSymbol: instrument.TradingSymbol,
                TransactionType: transactionType,
                Quantity: order.Quantity * instrument.LotSize,
                Product: LiveSettings.DefaultProduct,
                OrderType: Constants.OrderType.Market,
                Variety: Constants.Variety.Regular);

            order.Status = LiveOrderStatus.Submitted;
            order.BrokerOrderId = response.OrderId;
            order.SubmittedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Live order {OrderId} submitted to Kite as {BrokerOrderId}: {Side} {Quantity} of {Symbol}",
                order.Id, order.BrokerOrderId, order.Side, order.Quantity, instrument.TradingSymbol);

            return LiveOrderSubmissionResult.Submitted(order);
        }
        catch (Exception ex) when (ex is KiteException or HttpRequestException)
        {
            order.Status = LiveOrderStatus.Failed;
            order.RejectionReason = ex.Message;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogError(ex, "Live order {OrderId} failed while placing with Kite", order.Id);
            return LiveOrderSubmissionResult.Rejected("KiteOrderFailed", ex.Message);
        }
    }

    private async Task<decimal> GetNotionalTradedTodayAsync(DateTime now, CancellationToken cancellationToken)
    {
        var todayStartUtc = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

        var todaysOrders = await dbContext.LiveOrders
            .Include(o => o.Instrument)
            .Include(o => o.Fill)
            .Where(o => o.RequestedAtUtc >= todayStartUtc
                        && o.Status != LiveOrderStatus.Rejected
                        && o.Status != LiveOrderStatus.Failed
                        && o.Status != LiveOrderStatus.PendingApproval)
            .ToListAsync(cancellationToken);

        return todaysOrders.Sum(o =>
            (o.Fill?.FillPrice ?? 0m) * (o.Instrument?.LotSize ?? 1) * o.Quantity);
    }

    private async Task<LiveOrderSubmissionResult> RejectAsync(SubmitLiveOrderRequest request, DateTime now, string errorCode, string errorMessage, CancellationToken cancellationToken)
    {
        var rejectedOrder = new LiveOrder
        {
            Id = Guid.NewGuid(),
            InstrumentId = request.InstrumentId,
            Side = request.Side,
            Quantity = request.Quantity,
            IdempotencyKey = request.IdempotencyKey,
            Source = request.Source,
            RequestedAtUtc = now,
            Status = LiveOrderStatus.Rejected,
            RejectionReason = errorMessage
        };

        dbContext.LiveOrders.Add(rejectedOrder);
        await dbContext.SaveChangesAsync(cancellationToken);

        return LiveOrderSubmissionResult.Rejected(errorCode, errorMessage);
    }

    private async Task<LiveOrderSubmissionResult> RejectExistingAsync(LiveOrder order, string errorCode, string errorMessage, CancellationToken cancellationToken)
    {
        order.Status = LiveOrderStatus.Rejected;
        order.RejectionReason = errorMessage;
        await dbContext.SaveChangesAsync(cancellationToken);

        return LiveOrderSubmissionResult.Rejected(errorCode, errorMessage);
    }
}
