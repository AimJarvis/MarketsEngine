using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class PaperAccountConfiguration : IEntityTypeConfiguration<PaperAccount>
{
    public void Configure(EntityTypeBuilder<PaperAccount> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.CashBalance).HasPrecision(18, 4);
        builder.Property(a => a.ReservedMargin).HasPrecision(18, 4);

        builder.Ignore(a => a.AvailableCash);

        builder.ToTable(t => t.HasCheckConstraint("CK_PaperAccount_ReservedMargin_NotNegative", "\"ReservedMargin\" >= 0"));
    }
}
