using AccountingAI.Domain.Accounting;
using AccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccountingAI.Infrastructure.SqlServer.Persistence.Configurations;

internal sealed class IrcReturnValueConfiguration : IEntityTypeConfiguration<IrcReturnValue>
{
    public void Configure(EntityTypeBuilder<IrcReturnValue> builder)
    {
        builder.ToTable("IrcReturnValues", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Field).HasMaxLength(IrcReturnValue.FieldMaxLength).IsRequired();
        builder.Property(item => item.Amount).HasPrecision(18, 4);
        builder.Property(item => item.Note).HasMaxLength(IrcReturnValue.NoteMaxLength);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Year, item.Field }).IsUnique();
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
