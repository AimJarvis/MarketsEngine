using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.LiveTrading;
using MarketsEngine.Domain.Trading;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Infrastructure.Services;

/// <summary>
/// The paper/live switch. LiveTrading:Enabled is read once per call so a config reload takes
/// effect on the very next order - callers (StrategyRunnerService, controllers) depend only on
/// ITradingOrderGateway and never know or care which mode is active.
/// </summary>
public class TradingOrderGateway(
    IOrderExecutionService paperOrderExecutionService,
    ILiveOrderExecutionService liveOrderExecutionService,
    IOptionsMonitor<LiveTradingSettings> liveSettingsMonitor) : ITradingOrderGateway
{
    private const string PaperMode = "Paper";
    private const string LiveMode = "Live";

    public async Task<GatewayOrderResult> SubmitAsync(GatewayOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!liveSettingsMonitor.CurrentValue.Enabled)
        {
            if (request.AccountId is null)
                return GatewayOrderResult.Rejected(PaperMode, "AccountIdRequired", "AccountId is required in paper mode.");

            var paperRequest = new SubmitOrderRequest(
                request.AccountId.Value, request.InstrumentId, request.Side, request.TradingStyle, request.Quantity, request.IdempotencyKey);

            var paperResult = await paperOrderExecutionService.SubmitOrderAsync(paperRequest, cancellationToken);

            return paperResult.Success && paperResult.Order is not null
                ? GatewayOrderResult.Filled(PaperMode, paperResult.Order.Id)
                : GatewayOrderResult.Rejected(PaperMode, paperResult.ErrorCode ?? "Unknown", paperResult.ErrorMessage ?? "Paper order was rejected.");
        }

        var liveRequest = new SubmitLiveOrderRequest(request.InstrumentId, request.Side, request.Quantity, request.IdempotencyKey, request.Source);
        var liveResult = await liveOrderExecutionService.SubmitOrderAsync(liveRequest, cancellationToken);

        if (!liveResult.Success || liveResult.Order is null)
            return GatewayOrderResult.Rejected(LiveMode, liveResult.ErrorCode ?? "Unknown", liveResult.ErrorMessage ?? "Live order was rejected.");

        return liveResult.PendingApproval
            ? GatewayOrderResult.Pending(LiveMode, liveResult.Order.Id)
            : GatewayOrderResult.Filled(LiveMode, liveResult.Order.Id);
    }
}
