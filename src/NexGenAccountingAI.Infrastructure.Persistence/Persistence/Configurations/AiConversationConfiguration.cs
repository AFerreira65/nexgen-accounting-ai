using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexGenAccountingAI.Infrastructure.Persistence.Persistence.Configurations;

internal sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable("AiConversations", "reporting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Title).HasMaxLength(AiConversation.TitleMaxLength).IsRequired();
        builder.HasIndex(item => new { item.CompanyId, item.UserId, item.UpdatedAtUtc });
        builder.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Messages).WithOne().HasForeignKey(message => message.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        builder.ToTable("AiMessages", "reporting");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Role).HasConversion<int>();
        builder.Property(item => item.Content).HasMaxLength(AiMessage.ContentMaxLength).IsRequired();
        builder.Property(item => item.Model).HasMaxLength(80);
        builder.HasIndex(item => new { item.ConversationId, item.Sequence }).IsUnique();
    }
}
