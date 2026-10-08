using NexGenAccountingAI.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class FirmTaskCommentConfiguration : IEntityTypeConfiguration<FirmTaskComment>
{
    public void Configure(EntityTypeBuilder<FirmTaskComment> builder)
    {
        builder.ToTable("FirmTaskComments", "accounting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Text).HasMaxLength(FirmTaskComment.TextMaxLength).IsRequired();
        builder.HasIndex(item => new { item.TaskId, item.CreatedAtUtc });
        builder.HasOne<FirmTask>().WithMany().HasForeignKey(item => item.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}
