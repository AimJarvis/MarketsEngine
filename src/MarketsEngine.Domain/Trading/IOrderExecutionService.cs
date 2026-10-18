using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Trading;

public interface IOrderExecutionService
{
    Task<OrderSubmissionResult> SubmitOrderAsync(SubmitOrderRequest request, CancellationToken cancellationToken = default);
}

public record SubmitOrderRequest(
    Guid AccountId,
    Guid InstrumentId,
    OrderSide Side,
    TradingStyle TradingStyle,
    int Quantity,
    string IdempotencyKey);

public record OrderSubmissionResult(bool Success, string? ErrorCode, string? ErrorMessage, PaperOrder? Order, PaperFill? Fill)
{
    public static OrderSubmissionResult Rejected(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null, null);

    public static OrderSubmissionResult Filled(PaperOrder order, PaperFill fill) =>
        new(true, null, null, order, fill);
}
