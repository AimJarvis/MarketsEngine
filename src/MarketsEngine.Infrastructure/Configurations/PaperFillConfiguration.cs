using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class PaperFillConfiguration : IEntityTypeConfiguration<PaperFill>
{
    public void Configure(EntityTypeBuilder<PaperFill> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FillPrice).HasPrecision(18, 4);
        builder.Property(f => f.Fees).HasPrecision(18, 4);
        builder.Property(f => f.SlippageAmount).HasPrecision(18, 4);

        builder.HasOne(f => f.Order)
            .WithOne(o => o.Fill)
            .HasForeignKey<PaperFill>(f => f.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.OrderId).IsUnique();
    }
}
