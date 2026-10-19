namespace MarketsEngine.Domain.Strategies;

public readonly record struct PricePoint(DateTime TimestampUtc, decimal Price);
