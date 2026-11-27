using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Data.Configurations;

// Database setup (columns, indexes) for the saved spec match results table.
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
