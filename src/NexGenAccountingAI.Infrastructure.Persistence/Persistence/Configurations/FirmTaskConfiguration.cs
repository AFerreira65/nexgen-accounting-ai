using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class FirmTaskConfiguration : IEntityTypeConfiguration<FirmTask>
{
    public void Configure(EntityTypeBuilder<FirmTask> builder)
    {
        builder.ToTable("FirmTasks", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Title).HasMaxLength(FirmTask.TitleMaxLength).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(FirmTask.DescriptionMaxLength);
        builder.Property(item => item.Origin).HasMaxLength(FirmTask.OriginMaxLength);
        builder.Property(item => item.Priority).HasConversion<int>();
        builder.Property(item => item.Status).HasConversion<int>();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CompanyId, item.Status });
        builder.HasIndex(item => new { item.AssigneeUserId, item.Status });
        // Uma tarefa que nasce de uma obrigação cria-se uma só vez por empresa.
        builder.HasIndex(item => new { item.CompanyId, item.Origin }).IsUnique().HasFilter("\"Origin\" IS NOT NULL");
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(item => item.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
