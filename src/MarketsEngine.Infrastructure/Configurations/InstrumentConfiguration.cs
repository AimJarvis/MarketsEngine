using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.TradingSymbol)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(i => i.TradingSymbol).IsUnique();

        builder.HasIndex(i => i.KiteInstrumentToken)
            .IsUnique()
            .HasFilter("\"KiteInstrumentToken\" IS NOT NULL");

        builder.Property(i => i.StrikePrice).HasPrecision(18, 4);

        builder.Property(i => i.DataSource)
            .IsRequired()
            .HasMaxLength(32);

        builder.ToTable(t => t.HasCheckConstraint("CK_Instrument_LotSize_Positive", "\"LotSize\" > 0"));
    }
}
