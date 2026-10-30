using MarketsEngine.Domain.Entities;
using System.Linq;

namespace MarketsEngine.Api.Contracts;

public record SubmitLiveOrderApiRequest(Guid InstrumentId, string Side, int Quantity, string IdempotencyKey);

public record RejectLiveOrderApiRequest(string Reason);

public record ModifyLiveOrderApiRequest(int Quantity);

public record LiveFillResponse(int FilledQuantity, decimal FillPrice, DateTime FilledAtUtc);

public record LiveOrderResponse(
    Guid Id, Guid InstrumentId, string Side, int Quantity, string Status, string Source,
    string? RejectionReason, string? BrokerOrderId, DateTime RequestedAtUtc,
    DateTime? ApprovedAtUtc, DateTime? SubmittedAtUtc, List<LiveFillResponse> Fills,
    decimal? StopLossPercent, decimal? TakeProfitPercent);

public record LivePositionResponse(
    Guid InstrumentId, int NetQuantity, decimal AverageEntryPrice, decimal RealizedPnL, DateTime LastUpdatedAtUtc,
    decimal? StopLossPercent, decimal? TakeProfitPercent);

public static class LiveOrderResponseMapping
{
    public static LiveOrderResponse ToResponse(this LiveOrder o) => new(
        o.Id, o.InstrumentId, o.Side.ToString(), o.Quantity, o.Status.ToString(), o.Source,
        o.RejectionReason, o.BrokerOrderId, o.RequestedAtUtc, o.ApprovedAtUtc, o.SubmittedAtUtc,
        o.Fills.Select(f => new LiveFillResponse(f.FilledQuantity, f.FillPrice, f.FilledAtUtc)).ToList(),
        o.StopLossPercent, o.TakeProfitPercent);

    public static LivePositionResponse ToResponse(this LivePosition p) => new(
        p.InstrumentId, p.NetQuantity, p.AverageEntryPrice, p.RealizedPnL, p.LastUpdatedAtUtc,
        p.StopLossPercent, p.TakeProfitPercent);
}
