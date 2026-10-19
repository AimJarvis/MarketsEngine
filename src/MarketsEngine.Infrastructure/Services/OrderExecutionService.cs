using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.Trading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Infrastructure.Services;

/// <summary>
/// Orchestrates a single order submission end to end (idempotency, validation, fill
/// simulation, position/margin/ledger updates) and persists it all in one SaveChangesAsync
/// call so it either fully commits or fully rolls back.
/// </summary>
public class OrderExecutionService(
    MarketsEngineDbContext dbContext,
    IOptions<TradingSettings> tradingSettingsOptions,
    TimeProvider timeProvider) : IOrderExecutionService
{
    private readonly TradingSettings _settings = tradingSettingsOptions.Value;

    public async Task<OrderSubmissionResult> SubmitOrderAsync(SubmitOrderRequest request, CancellationToken cancellationToken = default)
    {
        var existingOrder = await dbContext.PaperOrders
            .Include(o => o.Fill)
            .FirstOrDefaultAsync(o => o.IdempotencyKey == request.IdempotencyKey, cancellationToken);

        if (existingOrder is not null)
        {
            return existingOrder.Status == OrderStatus.Filled && existingOrder.Fill is not null
                ? OrderSubmissionResult.Filled(existingOrder, existingOrder.Fill)
                : OrderSubmissionResult.Rejected(
                    "DuplicateIdempotencyKey",
                    existingOrder.RejectionReason ?? "Order with this idempotency key was already rejected.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var account = await dbContext.PaperAccounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);
        if (account is null)
        {
            return OrderSubmissionResult.Rejected("AccountNotFound", "The specified account does not exist.");
        }

        var instrument = await dbContext.Instruments.FirstOrDefaultAsync(i => i.Id == request.InstrumentId, cancellationToken);
        if (instrument is null)
        {
            return OrderSubmissionResult.Rejected("InstrumentNotFound", "The specified instrument does not exist.");
        }

        if (request.Quantity <= 0)
        {
            // Not persisted: the PaperOrder.Quantity check constraint requires a positive value,
            // and there is nothing here worth auditing beyond a client-side input mistake.
            return OrderSubmissionResult.Rejected("InvalidQuantity", "Quantity must be greater than zero.");
        }

        if (!_settings.PaperTradingEnabled)
        {
            return await RejectAsync(request, now, "PaperTradingDisabled", "Paper trading is currently disabled by the kill switch.", cancellationToken);
        }

        if (instrument.ExpiryDate is { } expiryDate && expiryDate < DateOnly.FromDateTime(now))
        {
            return await RejectAsync(request, now, "InstrumentExpired", "This instrument has expired and can no longer be traded.", cancellationToken);
        }

        var latestQuote = await dbContext.MarketQuotes
            .Where(q => q.InstrumentId == request.InstrumentId)
            .OrderByDescending(q => q.QuotedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestQuote is null)
        {
            return await RejectAsync(request, now, "NoQuoteAvailable", "No quote is available for this instrument.", cancellationToken);
        }

        var quoteAgeSeconds = (now - latestQuote.QuotedAtUtc).TotalSeconds;
        if (quoteAgeSeconds > _settings.QuoteStalenessSeconds)
        {
            return await RejectAsync(
                request, now, "StaleQuote",
                $"Quote is {quoteAgeSeconds:F0}s old, exceeding the {_settings.QuoteStalenessSeconds}s staleness limit.",
                cancellationToken);
        }

        var position = await dbContext.PaperPositions
            .FirstOrDefaultAsync(p => p.AccountId == request.AccountId && p.InstrumentId == request.InstrumentId, cancellationToken);

        var currentNetQuantity = position?.NetQuantity ?? 0;
        var currentAveragePrice = position?.AverageEntryPrice ?? 0m;

        var fillPrice = FillPriceCalculator.CalculateFillPrice(request.Side, latestQuote.BidPrice, latestQuote.AskPrice, _settings.SlippageBasisPoints);
        var referencePrice = request.Side == OrderSide.Buy ? latestQuote.AskPrice : latestQuote.BidPrice;
        var slippageAmount = Math.Abs(fillPrice - referencePrice) * instrument.LotSize * request.Quantity;
        var fees = _settings.FeePerLot * request.Quantity;

        var positionUpdate = PositionUpdater.ApplyFill(
            currentNetQuantity, currentAveragePrice, request.Side, request.Quantity, fillPrice, instrument.LotSize);

        var additionalMargin = MarginCalculator.CalculateAdditionalMarginRequired(
            currentNetQuantity, positionUpdate.NewNetQuantity, _settings.ShortOptionMarginPerLot);
        var marginRelease = MarginCalculator.CalculateMarginRelease(
            currentNetQuantity, positionUpdate.NewNetQuantity, _settings.ShortOptionMarginPerLot);

        var tradeValue = fillPrice * instrument.LotSize * request.Quantity;
        var tradeCashFlow = request.Side == OrderSide.Buy ? -tradeValue : tradeValue;

        var cashAfterTrade = account.CashBalance + tradeCashFlow;
        var cashAfterFees = cashAfterTrade - fees;
        var newReservedMargin = account.ReservedMargin + additionalMargin - marginRelease;

        if (cashAfterFees < 0 || cashAfterFees < newReservedMargin)
        {
            return await RejectAsync(
                request, now, "InsufficientFunds",
                "Available cash is insufficient to cover this order and any required margin.",
                cancellationToken);
        }

        var order = new PaperOrder
        {
            Id = Guid.NewGuid(),
            AccountId = request.AccountId,
            InstrumentId = request.InstrumentId,
            Side = request.Side,
            TradingStyle = request.TradingStyle,
            Quantity = request.Quantity,
            Status = OrderStatus.Filled,
            IdempotencyKey = request.IdempotencyKey,
            RequestedAtUtc = now
        };

        var fill = new PaperFill
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FilledQuantity = request.Quantity,
            FillPrice = fillPrice,
            Fees = fees,
            SlippageAmount = slippageAmount,
            FilledAtUtc = now
        };

        order.Fill = fill;

        if (position is null)
        {
            position = new PaperPosition
            {
                Id = Guid.NewGuid(),
                AccountId = request.AccountId,
                InstrumentId = request.InstrumentId
            };
            dbContext.PaperPositions.Add(position);
        }

        position.NetQuantity = positionUpdate.NewNetQuantity;
        position.AverageEntryPrice = positionUpdate.NewAverageEntryPrice;
        position.RealizedPnL += positionUpdate.RealizedPnLDelta;
        position.LastUpdatedAtUtc = now;

        account.CashBalance = cashAfterFees;
        account.ReservedMargin = newReservedMargin;

        dbContext.CashLedgerEntries.Add(new CashLedgerEntry
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            EntryType = request.Side == OrderSide.Buy ? CashLedgerEntryType.TradeDebit : CashLedgerEntryType.TradeCredit,
            Amount = tradeCashFlow,
            BalanceAfter = cashAfterTrade,
            RelatedOrderId = order.Id,
            Description = $"{request.Side} {request.Quantity} lot(s) of {instrument.TradingSymbol} @ {fillPrice:F2}",
            CreatedAtUtc = now
        });

        dbContext.CashLedgerEntries.Add(new CashLedgerEntry
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            EntryType = CashLedgerEntryType.Fee,
            Amount = -fees,
            BalanceAfter = cashAfterFees,
            RelatedOrderId = order.Id,
            Description = $"Fees for order {order.Id}",
            CreatedAtUtc = now
        });

        if (additionalMargin > 0)
        {
            dbContext.CashLedgerEntries.Add(new CashLedgerEntry
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                EntryType = CashLedgerEntryType.MarginBlock,
                Amount = -additionalMargin,
                BalanceAfter = cashAfterFees,
                RelatedOrderId = order.Id,
                Description = $"Margin blocked for short position in {instrument.TradingSymbol}",
                CreatedAtUtc = now
            });
        }

        if (marginRelease > 0)
        {
            dbContext.CashLedgerEntries.Add(new CashLedgerEntry
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                EntryType = CashLedgerEntryType.MarginRelease,
                Amount = marginRelease,
                BalanceAfter = cashAfterFees,
                RelatedOrderId = order.Id,
                Description = $"Margin released from reduced short position in {instrument.TradingSymbol}",
                CreatedAtUtc = now
            });
        }

        dbContext.PaperOrders.Add(order);
        dbContext.PaperFills.Add(fill);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return OrderSubmissionResult.Rejected("DuplicateIdempotencyKey", "An order with this idempotency key is already being processed.");
        }

        return OrderSubmissionResult.Filled(order, fill);
    }

    private async Task<OrderSubmissionResult> RejectAsync(
        SubmitOrderRequest request, DateTime now, string errorCode, string errorMessage, CancellationToken cancellationToken)
    {
        var rejectedOrder = new PaperOrder
        {
            Id = Guid.NewGuid(),
            AccountId = request.AccountId,
            InstrumentId = request.InstrumentId,
            Side = request.Side,
            TradingStyle = request.TradingStyle,
            Quantity = request.Quantity,
            Status = OrderStatus.Rejected,
            RejectionReason = errorMessage,
            IdempotencyKey = request.IdempotencyKey,
            RequestedAtUtc = now
        };

        dbContext.PaperOrders.Add(rejectedOrder);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return OrderSubmissionResult.Rejected("DuplicateIdempotencyKey", "An order with this idempotency key is already being processed.");
        }

        return OrderSubmissionResult.Rejected(errorCode, errorMessage);
    }
}
