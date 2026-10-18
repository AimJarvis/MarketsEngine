using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class PaperOrderConfiguration : IEntityTypeConfiguration<PaperOrder>
{
    public void Configure(EntityTypeBuilder<PaperOrder> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(o => o.IdempotencyKey).IsUnique();

        builder.Property(o => o.RejectionReason).HasMaxLength(256);

        builder.HasOne(o => o.Account)
            .WithMany()
            .HasForeignKey(o => o.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Instrument)
            .WithMany()
            .HasForeignKey(o => o.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => new { o.AccountId, o.RequestedAtUtc });

        builder.ToTable(t => t.HasCheckConstraint("CK_PaperOrder_Quantity_Positive", "\"Quantity\" > 0"));
    }
}
