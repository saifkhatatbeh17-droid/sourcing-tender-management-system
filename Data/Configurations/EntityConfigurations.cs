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

public sealed class ReportAccessRuleConfiguration : IEntityTypeConfiguration<ReportAccessRule>
{
    public void Configure(EntityTypeBuilder<ReportAccessRule> entity)
    {
        entity.ToTable("ReportAccessRules", "dbo");
        entity.HasKey(rule => rule.ReportAccessRuleId);
        entity.Property(rule => rule.ReportKey).HasMaxLength(100).IsRequired();
        entity.Property(rule => rule.Role).HasMaxLength(64).IsRequired();
        // Keep the updater ID as history even if the account is later removed.
        entity.Property(rule => rule.UpdatedByUserId).HasMaxLength(450).IsRequired();
        entity.HasIndex(rule => new { rule.ReportKey, rule.Role }).IsUnique();
    }
}

internal sealed class ReportsDataConfiguration :
    IEntityTypeConfiguration<AuditEntry>, IEntityTypeConfiguration<TenderContract>, IEntityTypeConfiguration<SupplierDocument>
{
    public void Configure(EntityTypeBuilder<AuditEntry> entity)
    {
        entity.ToTable("AuditEntries", "dbo");
        entity.HasIndex(x => x.OccurredAt);
        entity.HasIndex(x => new { x.EntityType, x.EntityId });
        entity.HasIndex(x => x.TenderId);
        entity.HasIndex(x => x.UserId);
        entity.Property(x => x.UserId).HasMaxLength(450);
        entity.Property(x => x.UserName).HasMaxLength(500);
        entity.Property(x => x.Role).HasMaxLength(100);
        entity.Property(x => x.Action).HasMaxLength(100);
        entity.Property(x => x.EntityType).HasMaxLength(100);
        entity.Property(x => x.EntityId).HasMaxLength(450);
        entity.Property(x => x.Summary).HasMaxLength(500);
        entity.Property(x => x.IpAddress).HasMaxLength(64);
    }

    public void Configure(EntityTypeBuilder<TenderContract> entity)
    {
        entity.HasIndex(x => x.TenderId).IsUnique();
        entity.HasOne<Tender>().WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<Bid>().WithMany().HasForeignKey(x => x.BidId).OnDelete(DeleteBehavior.NoAction);
        entity.Property(x => x.GeneratedByUserId).HasMaxLength(450);
        entity.Property(x => x.SignedUploadedByUserId).HasMaxLength(450);
        entity.Property(x => x.SignedFileName).HasMaxLength(255);
        entity.Property(x => x.SignedStoredFileName).HasMaxLength(255);
        entity.Property(x => x.SignedContentType).HasMaxLength(100);
        entity.Property(x => x.Notes).HasMaxLength(1000);
    }

    public void Configure(EntityTypeBuilder<SupplierDocument> entity)
    {
        entity.Property(x => x.VerificationStatus).HasMaxLength(20).HasDefaultValue("Pending");
        entity.Property(x => x.VerifiedByUserId).HasMaxLength(450);
        entity.Property(x => x.VerificationNote).HasMaxLength(1000);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.VerifiedByUserId).OnDelete(DeleteBehavior.NoAction);
        entity.ToTable(t => t.HasCheckConstraint("CK_SupplierDocuments_VerificationStatus",
            "[VerificationStatus] IN ('Pending','Verified','Rejected')"));
    }
}

public sealed class SpecMatchAnalysisConfiguration : IEntityTypeConfiguration<SpecMatchAnalysis>
{
    public void Configure(EntityTypeBuilder<SpecMatchAnalysis> entity)
    {
        entity.ToTable("SpecMatchAnalyses", "dbo");
        entity.HasKey(x => x.SpecMatchAnalysisId);
        entity.HasIndex(x => new { x.TenderId, x.SpecMatchAnalysisId });
        entity.Property(x => x.Fingerprint).HasMaxLength(64);
        entity.Property(x => x.ModelName).HasMaxLength(200);
        entity.Property(x => x.CreatedByUserId).HasMaxLength(450);
        entity.HasOne<Tender>().WithMany().HasForeignKey(x => x.TenderId);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class UserFaceCredentialConfiguration : IEntityTypeConfiguration<UserFaceCredential>
{
    public void Configure(EntityTypeBuilder<UserFaceCredential> entity)
    {
        entity.ToTable("UserFaceCredentials", "dbo", table =>
            table.HasCheckConstraint("CK_UserFaceCredentials_EnrollmentState", """
                (
                    [SampleCount] = 0
                    AND [ProtectedTemplates] IS NULL
                    AND [TemplateFormatVersion] IS NULL
                    AND [ModelIdentifier] IS NULL
                    AND [ConsentVersion] IS NULL
                    AND [ConsentGrantedAt] IS NULL
                    AND [EnrolledAt] IS NULL
                )
                OR
                (
                    [SampleCount] BETWEEN 3 AND 5
                    AND [ProtectedTemplates] IS NOT NULL
                    AND DATALENGTH([ProtectedTemplates]) > 0
                    AND [TemplateFormatVersion] IS NOT NULL
                    AND [TemplateFormatVersion] > 0
                    AND [ModelIdentifier] IS NOT NULL
                    AND LEN([ModelIdentifier]) > 0
                    AND [ConsentVersion] IS NOT NULL
                    AND LEN([ConsentVersion]) > 0
                    AND [ConsentGrantedAt] IS NOT NULL
                    AND [EnrolledAt] IS NOT NULL
                )
                """));

        entity.HasKey(x => x.UserId);
        entity.Property(x => x.UserId).HasMaxLength(450);
        entity.Property(x => x.ProtectedTemplates).HasColumnType("varbinary(max)");
        entity.Property(x => x.ModelIdentifier).HasMaxLength(150);
        entity.Property(x => x.ConsentVersion).HasMaxLength(50);
        entity.Property(x => x.SampleCount).HasDefaultValue((short)0);
        entity.Property(x => x.EnrollmentPromptedAt).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.ConsentGrantedAt).HasColumnType("datetime2(7)");
        entity.Property(x => x.EnrolledAt).HasColumnType("datetime2(7)");
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasOne(x => x.User).WithOne()
            .HasForeignKey<UserFaceCredential>(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
