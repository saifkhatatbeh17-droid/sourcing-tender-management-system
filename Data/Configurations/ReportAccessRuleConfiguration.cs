using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Data.Configurations;

// Database setup (columns, indexes) for the report access rules table.
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
