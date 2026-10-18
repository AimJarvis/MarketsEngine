using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Trading;

public static class PositionUpdater
{
    public readonly record struct Result(int NewNetQuantity, decimal NewAverageEntryPrice, decimal RealizedPnLDelta);

    /// <summary>
    /// Applies one fill to an existing position using average-cost tracking: opening/increasing
    /// a position re-weights the average entry price, reducing/flipping it realizes P&amp;L on the
    /// closed portion and (if the fill flips the sign) opens the remainder at the fill price.
    /// </summary>
    public static Result ApplyFill(
        int currentNetQuantity,
        decimal currentAverageEntryPrice,
        OrderSide side,
        int filledQuantity,
        decimal fillPrice,
        int lotSize)
    {
        var signedQuantity = side == OrderSide.Buy ? filledQuantity : -filledQuantity;
        var newNetQuantity = currentNetQuantity + signedQuantity;
        var newAverageEntryPrice = currentAverageEntryPrice;
        var realizedPnLDelta = 0m;

        var isIncreasingOrOpening = currentNetQuantity == 0 || Math.Sign(currentNetQuantity) == Math.Sign(signedQuantity);

        if (isIncreasingOrOpening)
        {
            var existingUnits = Math.Abs(currentNetQuantity) * lotSize;
            var newUnits = filledQuantity * lotSize;
            var totalUnits = existingUnits + newUnits;
            newAverageEntryPrice = totalUnits == 0
                ? fillPrice
                : ((currentAverageEntryPrice * existingUnits) + (fillPrice * newUnits)) / totalUnits;
        }
        else
        {
            var closingQuantity = Math.Min(Math.Abs(signedQuantity), Math.Abs(currentNetQuantity));
            var closingUnits = closingQuantity * lotSize;
            var positionWasLong = currentNetQuantity > 0;
            realizedPnLDelta = positionWasLong
                ? (fillPrice - currentAverageEntryPrice) * closingUnits
                : (currentAverageEntryPrice - fillPrice) * closingUnits;

            var remainingAfterClose = Math.Abs(signedQuantity) - closingQuantity;
            if (remainingAfterClose > 0)
            {
                newAverageEntryPrice = fillPrice;
            }
            else if (newNetQuantity == 0)
            {
                newAverageEntryPrice = 0m;
            }
        }

        return new Result(newNetQuantity, newAverageEntryPrice, realizedPnLDelta);
    }
}
