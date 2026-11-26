using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SourcingTenderManagement.Data.Configurations;

/// <summary>Company profile, department budgets and purchase history used as AI business context.</summary>
public sealed class CompanyContextConfiguration :
    IEntityTypeConfiguration<CompanyProfile>, IEntityTypeConfiguration<DepartmentBudget>, IEntityTypeConfiguration<PastPurchase>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> entity)
    {
        entity.ToTable("CompanyProfiles", "dbo", table =>
            table.HasCheckConstraint("CK_CompanyProfiles_FiscalMonth", "[FiscalYearStartMonth] BETWEEN 1 AND 12"));
        entity.Property(x => x.Name).HasMaxLength(200);
        entity.Property(x => x.Industry).HasMaxLength(200);
        entity.Property(x => x.Country).HasMaxLength(100);
        entity.Property(x => x.Currency).HasMaxLength(3);
        entity.Property(x => x.AnnualRevenue).HasPrecision(18, 2);
        entity.Property(x => x.AnnualProcurementBudget).HasPrecision(18, 2);
        entity.Property(x => x.Sites).HasMaxLength(1000);
        entity.Property(x => x.Overview).HasMaxLength(2000);
    }

    public void Configure(EntityTypeBuilder<DepartmentBudget> entity)
    {
        entity.ToTable("DepartmentBudgets", "dbo");
        entity.HasIndex(x => new { x.DepartmentId, x.FiscalYear }).IsUnique();
        entity.Property(x => x.Currency).HasMaxLength(3);
        entity.Property(x => x.AllocatedAmount).HasPrecision(18, 2);
        entity.Property(x => x.SpentToDate).HasPrecision(18, 2);
        entity.Property(x => x.CommittedAmount).HasPrecision(18, 2);
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId);
    }

    public void Configure(EntityTypeBuilder<PastPurchase> entity)
    {
        entity.ToTable("PastPurchases", "dbo");
        entity.HasIndex(x => new { x.DepartmentId, x.PurchasedOn });
        entity.Property(x => x.ItemName).HasMaxLength(300);
        entity.Property(x => x.Category).HasMaxLength(100);
        entity.Property(x => x.SupplierName).HasMaxLength(200);
        entity.Property(x => x.Currency).HasMaxLength(3);
        entity.Property(x => x.Quantity).HasPrecision(18, 2);
        entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
        entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId);
    }
}
