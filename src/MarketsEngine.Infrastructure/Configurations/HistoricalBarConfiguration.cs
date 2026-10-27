using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class HistoricalBarConfiguration : IEntityTypeConfiguration<HistoricalBar>
{
    public void Configure(EntityTypeBuilder<HistoricalBar> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Interval).IsRequired().HasMaxLength(16);
        builder.Property(b => b.Open).HasPrecision(18, 4);
        builder.Property(b => b.High).HasPrecision(18, 4);
        builder.Property(b => b.Low).HasPrecision(18, 4);
        builder.Property(b => b.Close).HasPrecision(18, 4);

        builder.HasOne(b => b.Instrument)
            .WithMany()
            .HasForeignKey(b => b.InstrumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => new { b.InstrumentId, b.TimestampUtc, b.Interval }).IsUnique();
    }
}
