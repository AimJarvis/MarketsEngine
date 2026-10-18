namespace MarketsEngine.Domain.Trading;

/// <summary>
/// Milestone-1 simplification: blocks a flat configurable margin per net-short lot so that
/// option-selling proceeds are never treated as free spendable cash. This is NOT real
/// SPAN/exposure margin and must be replaced before this system is used beyond paper trading.
/// </summary>
public static class MarginCalculator
{
    public static decimal CalculateAdditionalMarginRequired(int currentNetQuantity, int newNetQuantity, decimal marginPerShortLot)
    {
        var currentShortLots = Math.Max(0, -currentNetQuantity);
        var newShortLots = Math.Max(0, -newNetQuantity);
        var additionalShortLots = Math.Max(0, newShortLots - currentShortLots);
        return additionalShortLots * marginPerShortLot;
    }

    public static decimal CalculateMarginRelease(int currentNetQuantity, int newNetQuantity, decimal marginPerShortLot)
    {
        var currentShortLots = Math.Max(0, -currentNetQuantity);
        var newShortLots = Math.Max(0, -newNetQuantity);
        var releasedShortLots = Math.Max(0, currentShortLots - newShortLots);
        return releasedShortLots * marginPerShortLot;
    }
}
