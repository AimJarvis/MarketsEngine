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
            .WithOne(o => o.Fill)
            .HasForeignKey<LiveFill>(f => f.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.OrderId).IsUnique();
    }
}
