using MarketsEngine.Domain.Entities;

namespace MarketsEngine.Api.Contracts;

public record InstrumentResponse(
    Guid Id, string TradingSymbol, string Kind, string? Underlying, string? OptionType,
    decimal StrikePrice, DateOnly? ExpiryDate, int LotSize, string DataSource);

public record QuoteResponse(
    Guid InstrumentId, decimal BidPrice, decimal AskPrice, decimal LastTradedPrice,
    string DataSource, DateTime QuotedAtUtc);

public record CreateAccountRequest(string Name, decimal? InitialCapital);

public record AccountResponse(
    Guid Id, string Name, decimal CashBalance, decimal ReservedMargin, decimal AvailableCash, DateTime CreatedAtUtc);

public record PositionResponse(
    Guid InstrumentId, string TradingSymbol, int NetQuantity, decimal AverageEntryPrice,
    decimal RealizedPnL, decimal? UnrealizedPnL, DateTime LastUpdatedAtUtc,
    decimal? StopLossPercent, decimal? TakeProfitPercent);

public record AccountPnLResponse(Guid AccountId, decimal TotalRealizedPnL, decimal TotalUnrealizedPnL, decimal NetPnL);

public record SubmitOrderApiRequest(
    Guid AccountId, Guid InstrumentId, string Side, string TradingStyle, int Quantity, string IdempotencyKey);

public record FillResponse(decimal FillPrice, int FilledQuantity, decimal Fees, decimal SlippageAmount, DateTime FilledAtUtc);

public record OrderResponse(
    Guid Id, Guid AccountId, Guid InstrumentId, string Side, string TradingStyle, int Quantity,
    string Status, string? RejectionReason, DateTime RequestedAtUtc, FillResponse? Fill);

public static class ResponseMapping
{
    public static InstrumentResponse ToResponse(this Instrument i) =>
        new(i.Id, i.TradingSymbol, i.Kind.ToString(), i.Underlying?.ToString(), i.OptionType?.ToString(), i.StrikePrice, i.ExpiryDate, i.LotSize, i.DataSource);

    public static QuoteResponse ToResponse(this MarketQuote q) =>
        new(q.InstrumentId, q.BidPrice, q.AskPrice, q.LastTradedPrice, q.DataSource, q.QuotedAtUtc);

    public static AccountResponse ToResponse(this PaperAccount a) =>
        new(a.Id, a.Name, a.CashBalance, a.ReservedMargin, a.AvailableCash, a.CreatedAtUtc);

    public static OrderResponse ToResponse(this PaperOrder o) => new(
        o.Id, o.AccountId, o.InstrumentId, o.Side.ToString(), o.TradingStyle.ToString(), o.Quantity,
        o.Status.ToString(), o.RejectionReason, o.RequestedAtUtc,
        o.Fill is null ? null : new FillResponse(o.Fill.FillPrice, o.Fill.FilledQuantity, o.Fill.Fees, o.Fill.SlippageAmount, o.Fill.FilledAtUtc));
}
