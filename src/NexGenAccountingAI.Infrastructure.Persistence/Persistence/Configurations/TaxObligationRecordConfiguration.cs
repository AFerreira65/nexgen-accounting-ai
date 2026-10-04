using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class TaxObligationRecordConfiguration : IEntityTypeConfiguration<TaxObligationRecord>
{
    public void Configure(EntityTypeBuilder<TaxObligationRecord> builder)
    {
        builder.ToTable("TaxObligationRecords", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(TaxObligationRecord.CodeMaxLength).IsRequired();
        builder.Property(item => item.PeriodKey).HasMaxLength(TaxObligationRecord.PeriodKeyMaxLength).IsRequired();
        builder.Property(item => item.State).HasConversion<int>();
        builder.Property(item => item.Note).HasMaxLength(TaxObligationRecord.NoteMaxLength);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Code, item.PeriodKey }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
