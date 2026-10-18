using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Entities;

public class CashLedgerEntry
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public PaperAccount? Account { get; set; }
    public CashLedgerEntryType EntryType { get; set; }

    /// <summary>Signed: positive increases cash balance, negative decreases it.</summary>
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public Guid? RelatedOrderId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
