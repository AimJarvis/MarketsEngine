namespace MarketsEngine.Api.Contracts;

public record LoginUrlResponse(string LoginUrl);

public record KiteCallbackResponse(bool Success, string? BrokerUserId, DateTime GeneratedAtUtc);

public record SyncInstrumentsResponse(int InstrumentsSynced);

public record BrokerStatusResponse(
    bool IsAuthenticated, string? BrokerUserId, DateTime? SessionGeneratedAtUtc, bool IsTickerConnected);
