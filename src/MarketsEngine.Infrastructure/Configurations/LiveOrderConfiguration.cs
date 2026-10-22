using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class LiveOrderConfiguration : IEntityTypeConfiguration<LiveOrder>
{
    public void Configure(EntityTypeBuilder<LiveOrder> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(o => o.IdempotencyKey).IsUnique();

        builder.Property(o => o.Source).IsRequired().HasMaxLength(64);
        builder.Property(o => o.RejectionReason).HasMaxLength(256);
        builder.Property(o => o.BrokerOrderId).HasMaxLength(64);

        builder.HasOne(o => o.Instrument)
            .WithMany()
            .HasForeignKey(o => o.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.RequestedAtUtc);
        builder.HasIndex(o => o.Status);

        builder.ToTable(t => t.HasCheckConstraint("CK_LiveOrder_Quantity_Positive", "\"Quantity\" > 0"));
    }
}
