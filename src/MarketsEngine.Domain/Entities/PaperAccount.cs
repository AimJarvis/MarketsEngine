namespace MarketsEngine.Domain.Entities;

public class PaperAccount
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal CashBalance { get; set; }
    public decimal ReservedMargin { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public decimal AvailableCash => CashBalance - ReservedMargin;
}
