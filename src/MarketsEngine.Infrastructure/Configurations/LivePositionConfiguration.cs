using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class LivePositionConfiguration : IEntityTypeConfiguration<LivePosition>
{
    public void Configure(EntityTypeBuilder<LivePosition> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.AverageEntryPrice).HasPrecision(18, 4);
        builder.Property(p => p.RealizedPnL).HasPrecision(18, 4);

        builder.HasOne(p => p.Instrument)
            .WithMany()
            .HasForeignKey(p => p.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.InstrumentId).IsUnique();
    }
}
