using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Data.Configurations;

public sealed class AiInteractionConfiguration : IEntityTypeConfiguration<AiInteraction>
{
    public void Configure(EntityTypeBuilder<AiInteraction> entity)
    {
        entity.ToTable("AiInteractions", "dbo", table =>
            table.HasCheckConstraint("CK_AiInteractions_Outcome",
                "[Outcome] IN ('Succeeded', 'Failed', 'Cancelled', 'LimitReached')"));

        entity.HasKey(x => x.AiInteractionId);

        // No foreign key to users: anonymous calls have no user, and the audit trail must outlive account changes.
        entity.Property(x => x.UserId).HasMaxLength(450);
        entity.Property(x => x.Workspace).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Feature).HasMaxLength(64).IsRequired();
        entity.Property(x => x.SchemaName).HasMaxLength(100).IsRequired();
        entity.Property(x => x.PromptVersion).HasMaxLength(16).IsRequired();
        entity.Property(x => x.Model).HasMaxLength(200);
        entity.Property(x => x.Outcome).HasMaxLength(24).IsRequired();
        entity.Property(x => x.ErrorType).HasMaxLength(200);
        entity.Property(x => x.SchemaIssues).HasMaxLength(1000);
        entity.Property(x => x.InputSha256).HasMaxLength(64).IsRequired();
        entity.Property(x => x.OutputPreview).HasMaxLength(4000);
        entity.Property(x => x.RecordType).HasMaxLength(50);
        entity.Property(x => x.UserFeedback).HasMaxLength(24);

        entity.HasIndex(x => new { x.UserId, x.IsInteractive, x.CreatedAtUtc });
        entity.HasIndex(x => new { x.Feature, x.CreatedAtUtc });
    }
}
