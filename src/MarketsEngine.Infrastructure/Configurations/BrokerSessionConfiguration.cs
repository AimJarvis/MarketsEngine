using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MarketsEngine.Infrastructure.Configurations;

public class BrokerSessionConfiguration : IEntityTypeConfiguration<BrokerSession>
{
    public void Configure(EntityTypeBuilder<BrokerSession> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Provider).IsRequired().HasMaxLength(32);
        builder.Property(s => s.AccessToken).IsRequired().HasMaxLength(512);
        builder.Property(s => s.PublicToken).HasMaxLength(512);
        builder.Property(s => s.BrokerUserId).HasMaxLength(64);

        builder.HasIndex(s => s.Provider).IsUnique();
    }
}
