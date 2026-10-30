using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class LiveFillConfiguration : IEntityTypeConfiguration<LiveFill>
{
    public void Configure(EntityTypeBuilder<LiveFill> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FillPrice).HasPrecision(18, 4);

        builder.HasOne(f => f.Order)
            .WithMany(o => o.Fills)
            .HasForeignKey(f => f.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // The race-safety net described on LiveFill.CumulativeFilledQuantityAfter - not just
        // HasIndex(f => f.OrderId), since one order can now have many fills.
        builder.HasIndex(f => new { f.OrderId, f.CumulativeFilledQuantityAfter }).IsUnique();
    }
}
