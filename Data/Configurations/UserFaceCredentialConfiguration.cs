using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Data.Configurations;

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
