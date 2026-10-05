using NexGenAccountingAI.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class AtExchangeConfiguration : IEntityTypeConfiguration<AtExchange>
{
    public void Configure(EntityTypeBuilder<AtExchange> builder)
    {
        builder.ToTable("AtExchanges", "infrastructure");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Service).HasMaxLength(40).IsRequired();
        builder.Property(item => item.Operation).HasMaxLength(60).IsRequired();
        builder.Property(item => item.Period).HasMaxLength(20).IsRequired();
        builder.Property(item => item.Message).HasMaxLength(1000).IsRequired();
        builder.HasIndex(item => new { item.CompanyId, item.RequestedAtUtc });
    }
}
