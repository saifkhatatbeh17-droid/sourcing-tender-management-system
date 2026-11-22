using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Data.Configurations;

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
