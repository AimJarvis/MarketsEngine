using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class MarketQuoteConfiguration : IEntityTypeConfiguration<MarketQuote>
{
    public void Configure(EntityTypeBuilder<MarketQuote> builder)
    {
        builder.HasKey(q => q.Id);

        builder.Property(q => q.BidPrice).HasPrecision(18, 4);
        builder.Property(q => q.AskPrice).HasPrecision(18, 4);
        builder.Property(q => q.LastTradedPrice).HasPrecision(18, 4);

        builder.Property(q => q.DataSource)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasOne(q => q.Instrument)
            .WithMany()
            .HasForeignKey(q => q.InstrumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(q => new { q.InstrumentId, q.QuotedAtUtc });

        builder.ToTable(t => t.HasCheckConstraint("CK_MarketQuote_Ask_Gte_Bid", "\"AskPrice\" >= \"BidPrice\""));
    }
}
