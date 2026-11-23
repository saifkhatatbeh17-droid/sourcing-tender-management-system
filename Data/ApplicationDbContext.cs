using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Models.Entities;
namespace SourcingTenderManagement.Data;

// Edit the entities and this model, then create an EF migration. Do not scaffold from SQL.
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ReportAccessRule> ReportAccessRules { get; set; }
    public DbSet<AuditEntry> AuditEntries { get; set; }
    public DbSet<TenderContract> TenderContracts { get; set; }
    public DbSet<AiAnalysis> AiAnalyses { get; set; }
    public DbSet<SpecMatchAnalysis> SpecMatchAnalyses { get; set; }
    public DbSet<AiInteraction> AiInteractions { get; set; }
    public DbSet<BidGuarantee> BidGuarantees { get; set; }
    public DbSet<Bid> Bids { get; set; }
    public DbSet<BidDocument> BidDocuments { get; set; }
    public DbSet<BidItem> BidItems { get; set; }
    public DbSet<Clarification> Clarifications { get; set; }
    public DbSet<ClarificationResponse> ClarificationResponses { get; set; }
    public DbSet<CommitteeAssignment> CommitteeAssignments { get; set; }
    public DbSet<CommitteeWeightProposal> CommitteeWeightProposals { get; set; }
    public DbSet<CommitteeTieVote> CommitteeTieVotes { get; set; }
    public DbSet<ContactMessage> ContactMessages { get; set; }
    public DbSet<Department> Departments { get; set; }
    public DbSet<Evaluation> Evaluations { get; set; }
    public DbSet<EvaluationCriterion> EvaluationCriteria { get; set; }
    public DbSet<EvaluationReport> EvaluationReports { get; set; }
    public DbSet<EvaluationScore> EvaluationScores { get; set; }
    public DbSet<Invitation> Invitations { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<PurchaseRequest> PurchaseRequests { get; set; }
    public DbSet<PurchaseRequestItem> PurchaseRequestItems { get; set; }
    public DbSet<SupplierDocument> SupplierDocuments { get; set; }
    public DbSet<SupplierProfile> SupplierProfiles { get; set; }
    public DbSet<Tender> Tenders { get; set; }
    public DbSet<TenderApproval> TenderApprovals { get; set; }
    public DbSet<TenderDocument> TenderDocuments { get; set; }
    public DbSet<UserFaceCredential> UserFaceCredentials { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<BidGuarantee>(entity =>
        {
            entity.HasIndex(g => new { g.BidId, g.Kind }).IsUnique();
            entity.HasIndex(g => g.VerificationToken).IsUnique();
            entity.HasOne(g => g.Bid).WithMany().HasForeignKey(g => g.BidId).OnDelete(DeleteBehavior.NoAction);
            entity.Ignore(g => g.Label);
            entity.Ignore(g => g.DurationDays);
            entity.ToTable("BidGuarantees", table =>
            {
                table.HasCheckConstraint("CK_BidGuarantees_Kind", "[Kind] IN ('Legal', 'Bank')");
                table.HasCheckConstraint("CK_BidGuarantees_Dates", "[ExpiresOn] >= [StartsOn]");
                table.HasCheckConstraint("CK_BidGuarantees_Coverage", "[Coverage] IN ('Product', 'Service')");
            });
        });
        // Keep existing Identity column sizes when enabling passkey storage.
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>(entity =>
        {
            entity.Property(login => login.LoginProvider).Metadata.SetMaxLength(null);
            entity.Property(login => login.ProviderKey).Metadata.SetMaxLength(null);
        });
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>(entity =>
        {
            entity.Property(token => token.LoginProvider).Metadata.SetMaxLength(null);
            entity.Property(token => token.Name).Metadata.SetMaxLength(null);
        });
        modelBuilder.Entity<ApplicationUser>().Property(user => user.PhoneNumber).Metadata.SetMaxLength(null);
        modelBuilder.ApplyConfiguration(new Configurations.UserFaceCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.AiInteractionConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.ReportAccessRuleConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.SpecMatchAnalysisConfiguration());
        var reportsData = new Configurations.ReportsDataConfiguration();
        modelBuilder.ApplyConfiguration<AuditEntry>(reportsData);
        modelBuilder.ApplyConfiguration<TenderContract>(reportsData);
        modelBuilder.ApplyConfiguration<SupplierDocument>(reportsData);
        modelBuilder.Entity<CommitteeWeightProposal>(entity =>
        {
            entity.Property(p => p.FinancialWeight).HasPrecision(5, 2);
            entity.HasIndex(p => new { p.TenderId, p.CommitteeAssignmentId }).IsUnique();
            entity.HasOne(p => p.Tender).WithMany(t => t.CommitteeWeightProposals).HasForeignKey(p => p.TenderId);
            entity.HasOne(p => p.CommitteeAssignment).WithMany()
                .HasPrincipalKey(a => new { a.TenderId, a.CommitteeAssignmentId })
                .HasForeignKey(p => new { p.TenderId, p.CommitteeAssignmentId });
            entity.ToTable("CommitteeWeightProposals", table => table.HasCheckConstraint("CK_CommitteeWeightProposals_Weight", "[FinancialWeight] > 0 AND [FinancialWeight] < 100"));
        });
        modelBuilder.Entity<Tender>().Property(t => t.CommitteeFinancialWeight).HasPrecision(5, 2);
        modelBuilder.Entity<Tender>(entity =>
        {
            entity.Property(t => t.TopEditState).HasMaxLength(30);
            entity.Property(t => t.TopEditRequestedByUserId).HasMaxLength(450);
            entity.Property(t => t.TopEditRequestNotes).HasMaxLength(4000);
            entity.Property(t => t.TopEditManagerNotes).HasMaxLength(4000);
            entity.Property(t => t.TopEditOfficerNotes).HasMaxLength(4000);
        });
        modelBuilder.Entity<CommitteeTieVote>(entity =>
        {
            entity.HasIndex(v => new { v.TenderId, v.CommitteeAssignmentId }).IsUnique();
            entity.HasOne(v => v.Tender).WithMany(t => t.CommitteeTieVotes).HasForeignKey(v => v.TenderId);
            entity.HasOne(v => v.CommitteeAssignment).WithMany()
                .HasPrincipalKey(a => new { a.TenderId, a.CommitteeAssignmentId })
                .HasForeignKey(v => new { v.TenderId, v.CommitteeAssignmentId });
            entity.HasOne(v => v.Bid).WithMany().HasPrincipalKey(b => new { b.TenderId, b.BidId })
                .HasForeignKey(v => new { v.TenderId, v.BidId });
        });
        modelBuilder.Entity<Tender>().ToTable("Tenders", table => table.HasCheckConstraint("CK_Tenders_CommitteeWeightLock",
            "([CommitteeFinancialWeight] IS NULL AND [CommitteeWeightsLockedAt] IS NULL) OR ([CommitteeFinancialWeight] IS NOT NULL AND [CommitteeFinancialWeight] > 0 AND [CommitteeFinancialWeight] < 100 AND [CommitteeWeightsLockedAt] IS NOT NULL)"));
        modelBuilder.Entity<Evaluation>().Property(e => e.FinancialMark).HasPrecision(5, 2);
        modelBuilder.Entity<Evaluation>().Property(e => e.TechnicalMark).HasPrecision(5, 2);
        modelBuilder.Entity<Evaluation>().ToTable("Evaluations", table => table.HasCheckConstraint("CK_Evaluations_DirectMarks",
            "([FinancialMark] IS NULL AND [TechnicalMark] IS NULL) OR ([FinancialMark] IS NOT NULL AND [TechnicalMark] IS NOT NULL AND [FinancialMark] >= 0 AND [TechnicalMark] >= 0 AND [FinancialMark] + [TechnicalMark] <= 100)"));
        modelBuilder.Entity<ContactMessage>(entity =>
        {
            entity.ToTable("ContactMessages", "dbo");
            entity.Property(message => message.UserId).HasMaxLength(450);
            entity.Property(message => message.FullName).HasMaxLength(120).IsRequired();
            entity.Property(message => message.Email).HasMaxLength(254).IsRequired();
            entity.Property(message => message.Subject).HasMaxLength(150).IsRequired();
            entity.Property(message => message.Message).HasMaxLength(2000).IsRequired();
            entity.Property(message => message.Status).HasMaxLength(20).IsRequired();
        });
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FullName).HasMaxLength(150).IsRequired();
            entity.Property(u => u.IsActive).HasDefaultValue(true);
            entity.HasOne(u => u.Department).WithMany(d => d.Users)
                .HasForeignKey(u => u.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AiAnalysis>(entity =>
        {
            entity.Property(e => e.AiAnalysisId).UseIdentityColumn();
            entity.HasKey(e => e.AiAnalysisId);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.FinancialWeight).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.ModelName).HasMaxLength(100);
            entity.Property(e => e.RequestedByUserId).HasMaxLength(450);
            entity.Property(e => e.RequirementsWeight).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.RequestedByUser).WithMany(p => p.AiAnalyses)
                .HasForeignKey(d => d.RequestedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tender).WithMany(p => p.AiAnalyses)
                .HasForeignKey(d => d.TenderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Bid>(entity =>
        {
            entity.Property(e => e.BidId).UseIdentityColumn();
            entity.HasKey(e => e.BidId);

            entity.HasIndex(e => e.InvitationId).IsUnique();

            entity.HasIndex(e => new { e.TenderId, e.BidId }).IsUnique();

            entity.Property(e => e.AdditionalCosts).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ComplianceNotes).HasMaxLength(2000);
            entity.Property(e => e.ComplianceReviewedByUserId).HasMaxLength(450);
            entity.Property(e => e.ComplianceStatus).HasMaxLength(30);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.PaymentTerms).HasMaxLength(1000);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("Draft");
            entity.Property(e => e.TaxAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TechnicalDetails).HasMaxLength(4000);

            entity.HasOne(d => d.ComplianceReviewedByUser).WithMany(p => p.Bids)
                .HasForeignKey(d => d.ComplianceReviewedByUserId);

            entity.HasOne(d => d.Invitation).WithMany(p => p.Bids)
                .HasPrincipalKey(p => new { p.InvitationId, p.TenderId })
                .HasForeignKey(d => new { d.InvitationId, d.TenderId })
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<BidDocument>(entity =>
        {
            entity.Property(e => e.BidDocumentId).UseIdentityColumn();
            entity.HasKey(e => e.BidDocumentId);

            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.DocumentType).HasMaxLength(100);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.StoredFileName).HasMaxLength(255);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Bid).WithMany(p => p.BidDocuments)
                .HasForeignKey(d => d.BidId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<BidItem>(entity =>
        {
            entity.Property(e => e.BidItemId).UseIdentityColumn();
            entity.HasKey(e => e.BidItemId);

            entity.HasIndex(e => new { e.BidId, e.PurchaseRequestItemId }).IsUnique();

            entity.Property(e => e.OfferedSpecifications).HasMaxLength(4000);
            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Bid).WithMany(p => p.BidItems)
                .HasForeignKey(d => d.BidId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.PurchaseRequestItem).WithMany(p => p.BidItems)
                .HasForeignKey(d => d.PurchaseRequestItemId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Clarification>(entity =>
        {
            entity.Property(e => e.ClarificationId).UseIdentityColumn();
            entity.HasKey(e => e.ClarificationId);

            entity.HasIndex(e => new { e.RecipientUserId, e.Status });

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.SenderUserId).HasMaxLength(450);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
            entity.Property(e => e.Subject).HasMaxLength(200);

            entity.HasOne(d => d.Bid).WithMany(p => p.Clarifications)
                .HasForeignKey(d => d.BidId);

            entity.HasOne(d => d.PurchaseRequest).WithMany(p => p.Clarifications)
                .HasForeignKey(d => d.PurchaseRequestId);

            entity.HasOne(d => d.RecipientUser).WithMany(p => p.ClarificationRecipientUsers)
                .HasForeignKey(d => d.RecipientUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.SenderUser).WithMany(p => p.ClarificationSenderUsers)
                .HasForeignKey(d => d.SenderUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tender).WithMany(p => p.Clarifications)
                .HasForeignKey(d => d.TenderId);
        });

        modelBuilder.Entity<ClarificationResponse>(entity =>
        {
            entity.Property(e => e.ClarificationResponseId).UseIdentityColumn();
            entity.HasKey(e => e.ClarificationResponseId);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Message).HasMaxLength(4000);
            entity.Property(e => e.RespondedByUserId).HasMaxLength(450);

            entity.HasOne(d => d.Clarification).WithMany(p => p.ClarificationResponses)
                .HasForeignKey(d => d.ClarificationId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.RespondedByUser).WithMany(p => p.ClarificationResponses)
                .HasForeignKey(d => d.RespondedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CommitteeAssignment>(entity =>
        {
            entity.Property(e => e.CommitteeAssignmentId).UseIdentityColumn();
            entity.HasKey(e => e.CommitteeAssignmentId);

            entity.HasIndex(e => new { e.TenderId, e.CommitteeAssignmentId, e.UserId, e.CommitteeType }).IsUnique();

            entity.HasIndex(e => new { e.TenderId, e.CommitteeType, e.Position }).IsUnique();

            entity.HasIndex(e => new { e.TenderId, e.CommitteeType, e.UserId }).IsUnique();

            entity.HasIndex(e => new { e.TenderId, e.CommitteeAssignmentId }).IsUnique();

            entity.Property(e => e.CommitteeType).HasMaxLength(20);

            entity.HasOne(d => d.Tender).WithMany(p => p.CommitteeAssignments)
                .HasForeignKey(d => d.TenderId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.User).WithMany(p => p.CommitteeAssignments)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.Property(e => e.DepartmentId).UseIdentityColumn();
            entity.HasKey(e => e.DepartmentId);

            entity.HasIndex(e => e.Name).IsUnique();

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Evaluation>(entity =>
        {
            entity.Property(e => e.EvaluationId).UseIdentityColumn();
            entity.HasKey(e => e.EvaluationId);

            entity.HasIndex(e => new { e.BidId, e.CommitteeAssignmentId }).IsUnique();

            entity.HasIndex(e => new { e.TenderId, e.EvaluationId }).IsUnique();

            entity.Property(e => e.Notes).HasMaxLength(4000);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Draft");

            entity.HasOne(d => d.Bid).WithMany(p => p.Evaluations)
                .HasPrincipalKey(p => new { p.TenderId, p.BidId })
                .HasForeignKey(d => new { d.TenderId, d.BidId })
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.CommitteeAssignment).WithMany(p => p.Evaluations)
                .HasPrincipalKey(p => new { p.TenderId, p.CommitteeAssignmentId })
                .HasForeignKey(d => new { d.TenderId, d.CommitteeAssignmentId })
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<EvaluationCriterion>(entity =>
        {
            entity.Property(e => e.EvaluationCriterionId).UseIdentityColumn();
            entity.HasKey(e => e.EvaluationCriterionId);

            entity.HasIndex(e => new { e.TenderId, e.EvaluationCriterionId }).IsUnique();

            entity.HasIndex(e => new { e.TenderId, e.Name }).IsUnique();

            entity.Property(e => e.Category).HasMaxLength(20);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Weight).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.Tender).WithMany(p => p.EvaluationCriteria)
                .HasForeignKey(d => d.TenderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<EvaluationReport>(entity =>
        {
            entity.Property(e => e.EvaluationReportId).UseIdentityColumn();
            entity.HasKey(e => e.EvaluationReportId);

            entity.HasIndex(e => e.TenderId).IsUnique();

            entity.Property(e => e.PreparedByUserId).HasMaxLength(450);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("Draft");

            entity.HasOne(d => d.PreparedByUser).WithMany(p => p.EvaluationReports)
                .HasForeignKey(d => d.PreparedByUserId);

            entity.HasOne(d => d.Tender).WithOne(p => p.EvaluationReport)
                .HasForeignKey<EvaluationReport>(d => d.TenderId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Bid).WithMany(p => p.EvaluationReports)
                .HasPrincipalKey(p => new { p.TenderId, p.BidId })
                .HasForeignKey(d => new { d.TenderId, d.RecommendedBidId });
        });

        modelBuilder.Entity<EvaluationScore>(entity =>
        {
            entity.Property(e => e.EvaluationScoreId).UseIdentityColumn();
            entity.HasKey(e => e.EvaluationScoreId);

            entity.HasIndex(e => new { e.EvaluationId, e.EvaluationCriterionId }).IsUnique();

            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.Score).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.EvaluationCriterion).WithMany(p => p.EvaluationScores)
                .HasPrincipalKey(p => new { p.TenderId, p.EvaluationCriterionId })
                .HasForeignKey(d => new { d.TenderId, d.EvaluationCriterionId })
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Evaluation).WithMany(p => p.EvaluationScores)
                .HasPrincipalKey(p => new { p.TenderId, p.EvaluationId })
                .HasForeignKey(d => new { d.TenderId, d.EvaluationId })
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Invitation>(entity =>
        {
            entity.Property(e => e.InvitationId).UseIdentityColumn();
            entity.HasKey(e => e.InvitationId);

            entity.HasIndex(e => new { e.InvitationId, e.TenderId }).IsUnique();

            entity.HasIndex(e => new { e.TenderId, e.SupplierProfileId }).IsUnique();

            entity.Property(e => e.InvitedByUserId).HasMaxLength(450);
            entity.Property(e => e.SentAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");

            entity.HasOne(d => d.InvitedByUser).WithMany(p => p.Invitations)
                .HasForeignKey(d => d.InvitedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.SupplierProfile).WithMany(p => p.Invitations)
                .HasForeignKey(d => d.SupplierProfileId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tender).WithMany(p => p.Invitations)
                .HasForeignKey(d => d.TenderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(e => e.NotificationId).UseIdentityColumn();
            entity.HasKey(e => e.NotificationId);

            entity.HasIndex(e => new { e.UserId, e.IsRead });

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Message).HasMaxLength(2000);
            entity.Property(e => e.RelativeUrl).HasMaxLength(500);
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PurchaseRequest>(entity =>
        {
            entity.Property(e => e.PurchaseRequestId).UseIdentityColumn();
            entity.HasKey(e => e.PurchaseRequestId);

            entity.HasIndex(e => e.DepartmentId);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Priority).HasMaxLength(20);
            entity.Property(e => e.Reason).HasMaxLength(2000);
            entity.Property(e => e.RequestedByUserId).HasMaxLength(450);
            entity.Property(e => e.ReviewNotes).HasMaxLength(2000);
            entity.Property(e => e.ReviewedByUserId).HasMaxLength(450);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("Draft");
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.Department).WithMany(p => p.PurchaseRequests)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.RequestedByUser).WithMany(p => p.PurchaseRequestRequestedByUsers)
                .HasForeignKey(d => d.RequestedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.ReviewedByUser).WithMany(p => p.PurchaseRequestReviewedByUsers)
                .HasForeignKey(d => d.ReviewedByUserId);
        });

        modelBuilder.Entity<PurchaseRequestItem>(entity =>
        {
            entity.Property(e => e.PurchaseRequestItemId).UseIdentityColumn();
            entity.HasKey(e => e.PurchaseRequestItemId);

            entity.Property(e => e.ItemName).HasMaxLength(200);
            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.Specifications).HasMaxLength(4000);
            entity.Property(e => e.Unit).HasMaxLength(30);

            entity.HasOne(d => d.PurchaseRequest).WithMany(p => p.PurchaseRequestItems)
                .HasForeignKey(d => d.PurchaseRequestId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SupplierDocument>(entity =>
        {
            entity.Property(e => e.SupplierDocumentId).UseIdentityColumn();
            entity.HasKey(e => e.SupplierDocumentId);

            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.DocumentType).HasMaxLength(100);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.StoredFileName).HasMaxLength(255);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.SupplierProfile).WithMany(p => p.SupplierDocuments)
                .HasForeignKey(d => d.SupplierProfileId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SupplierProfile>(entity =>
        {
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.SupplierProfileId).UseIdentityColumn();
            entity.HasKey(e => e.SupplierProfileId);

            entity.HasIndex(e => e.UserId).IsUnique();

            entity.HasIndex(e => e.RegistrationNumber).IsUnique();

            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.CompanyName).HasMaxLength(200);
            entity.Property(e => e.ContactName).HasMaxLength(150);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.RegistrationNumber).HasMaxLength(100);
            entity.Property(e => e.ReviewNotes).HasMaxLength(2000);
            entity.Property(e => e.ReviewedByUserId).HasMaxLength(450);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasDefaultValue("Pending");
            entity.Property(e => e.TaxNumber).HasMaxLength(100);

            entity.HasOne(d => d.ReviewedByUser).WithMany(p => p.SupplierProfileReviewedByUsers)
                .HasForeignKey(d => d.ReviewedByUserId);

            entity.HasOne(d => d.User).WithOne(p => p.SupplierProfileUser)
                .HasForeignKey<SupplierProfile>(d => d.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TenderOfferExtension>(entity =>
        {
            entity.ToTable("TenderOfferExtensions");
            entity.HasKey(e => new { e.TenderId, e.Round });
        });
        modelBuilder.Entity<Tender>(entity =>
        {
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.OfferShortfallReason).HasMaxLength(2000);
            entity.HasMany(e => e.OfferExtensions).WithOne().HasForeignKey(e => e.TenderId);

            entity.HasOne(e => e.ReplacesTender).WithMany().HasForeignKey(e => e.ReplacesTenderId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(e => e.ReplacesTenderId).IsUnique().HasFilter("[ReplacesTenderId] IS NOT NULL");
            entity.Property(e => e.RejectionForwardedByUserId).HasMaxLength(450);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.RejectionForwardedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.ToTable("Tenders", table => {
                table.HasCheckConstraint("CK_Tenders_Replacement", "[ReplacesTenderId] IS NULL OR [ReplacesTenderId] <> [TenderId]");
                table.HasCheckConstraint("CK_Tenders_RejectionForward", "([RejectionForwardedAt] IS NULL AND [RejectionForwardedByUserId] IS NULL) OR ([RejectionForwardedAt] IS NOT NULL AND [RejectionForwardedByUserId] IS NOT NULL)");
            });
            entity.Property(e => e.TenderId).UseIdentityColumn();
            entity.HasKey(e => e.TenderId);

            entity.Property(e => e.TenderId).ValueGeneratedOnAdd();
            entity.Property(e => e.AwardNotes).HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.CreatedByUserId).HasMaxLength(450);
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasDefaultValue("Draft");
            entity.Property(e => e.Terms).HasMaxLength(4000);
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.Tenders)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.PurchaseRequest).WithMany(p => p.Tenders)
                .HasForeignKey(d => d.PurchaseRequestId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Bid).WithMany(p => p.Tenders)
                .HasPrincipalKey(p => new { p.TenderId, p.BidId })
                .HasForeignKey(d => new { d.TenderId, d.AwardedBidId });
        });

        modelBuilder.Entity<TenderApproval>(entity =>
        {
            entity.Property(e => e.TenderApprovalId).UseIdentityColumn();
            entity.HasKey(e => e.TenderApprovalId);

            entity.HasIndex(e => new { e.TenderId, e.Stage, e.UserId }).IsUnique();

            entity.HasIndex(e => e.CommitteeAssignmentId)
                .IsUnique()
                .HasFilter("([CommitteeAssignmentId] IS NOT NULL)");

            entity.Property(e => e.DecidedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Decision).HasMaxLength(20);
            entity.Property(e => e.Notes).HasMaxLength(4000);
            entity.Property(e => e.Stage).HasMaxLength(20);

            entity.HasOne(d => d.Tender).WithMany(p => p.TenderApprovals)
                .HasForeignKey(d => d.TenderId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.User).WithMany(p => p.TenderApprovals)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.CommitteeAssignment).WithMany(p => p.TenderApprovalCommitteeAssignments)
                .HasPrincipalKey(p => new { p.TenderId, p.CommitteeAssignmentId })
                .HasForeignKey(d => new { d.TenderId, d.CommitteeAssignmentId });

            entity.HasOne(d => d.CommitteeAssignmentNavigation).WithMany(p => p.TenderApprovalCommitteeAssignmentNavigations)
                .HasPrincipalKey(p => new { p.TenderId, p.CommitteeAssignmentId, p.UserId, p.CommitteeType })
                .HasForeignKey(d => new { d.TenderId, d.CommitteeAssignmentId, d.UserId, d.Stage });
        });

        modelBuilder.Entity<TenderDocument>(entity =>
        {
            entity.Property(e => e.TenderDocumentId).UseIdentityColumn();
            entity.HasKey(e => e.TenderDocumentId);

            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.StoredFileName).HasMaxLength(255);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Tender).WithMany(p => p.TenderDocuments)
                .HasForeignKey(d => d.TenderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AiAnalysis>().ToTable("AiAnalyses", table =>
        {
            table.HasCheckConstraint("CK_AiAnalyses_1", "(isjson([ResultJson])=(1))");
            table.HasCheckConstraint("CK_AiAnalyses_2", "([FinancialWeight]>=0 AND [FinancialWeight]<=100 AND [RequirementsWeight]>=0 AND [RequirementsWeight]<=100 AND [FinancialWeight]+[RequirementsWeight]=100)");
        });

        modelBuilder.Entity<Bid>().ToTable("Bids", table =>
        {
            table.HasCheckConstraint("CK_Bids_1", "([AdditionalCosts]>=(0))");
            table.HasCheckConstraint("CK_Bids_2", "([ComplianceStatus]='PendingClarification' OR [ComplianceStatus]='NonCompliant' OR [ComplianceStatus]='Compliant')");
            table.HasCheckConstraint("CK_Bids_3", "([DeliveryDays]>=(0))");
            table.HasCheckConstraint("CK_Bids_4", "([Status]='Withdrawn' OR [Status]='Rejected' OR [Status]='Accepted' OR [Status]='NonCompliant' OR [Status]='Compliant' OR [Status]='UnderReview' OR [Status]='Submitted' OR [Status]='Draft')");
            table.HasCheckConstraint("CK_Bids_5", "([TaxAmount]>=(0))");
        });

        modelBuilder.Entity<BidDocument>().ToTable("BidDocuments", table =>
        {
            table.HasCheckConstraint("CK_BidDocuments_1", "([FileSizeBytes]>(0))");
        });

        modelBuilder.Entity<BidItem>().ToTable("BidItems", table =>
        {
            table.HasCheckConstraint("CK_BidItems_1", "([Quantity]>(0))");
            table.HasCheckConstraint("CK_BidItems_2", "([UnitPrice]>=(0))");
        });

        modelBuilder.Entity<Clarification>().ToTable("Clarifications", table =>
        {
            table.HasCheckConstraint("CK_Clarifications_1", "([Status]='Resolved' OR [Status]='Answered' OR [Status]='Pending')");
            table.HasCheckConstraint("CK_Clarifications_2", "([SenderUserId]<>[RecipientUserId])");
            table.HasCheckConstraint("CK_Clarifications_3", "(((case when [PurchaseRequestId] IS NULL then (0) else (1) end+case when [TenderId] IS NULL then (0) else (1) end)+case when [BidId] IS NULL then (0) else (1) end)=(1))");
        });

        modelBuilder.Entity<ClarificationResponse>().ToTable("ClarificationResponses");

        modelBuilder.Entity<CommitteeAssignment>().ToTable("CommitteeAssignments", table =>
        {
            table.HasCheckConstraint("CK_CommitteeAssignments_1", "([CommitteeType]='Top' OR [CommitteeType]='Evaluation')");
            table.HasCheckConstraint("CK_CommitteeAssignments_2", "([Position]>=(1) AND [Position]<=(4))");
        });

        modelBuilder.Entity<Department>().ToTable("Departments");

        modelBuilder.Entity<Evaluation>().ToTable("Evaluations", table =>
        {
            table.HasCheckConstraint("CK_Evaluations_1", "([Status]='Approved' OR [Status]='Submitted' OR [Status]='Draft')");
        });

        modelBuilder.Entity<EvaluationCriterion>().ToTable("EvaluationCriteria", table =>
        {
            table.HasCheckConstraint("CK_EvaluationCriteria_1", "([Category]='Requirements' OR [Category]='Financial')");
            table.HasCheckConstraint("CK_EvaluationCriteria_2", "([Weight]>(0) AND [Weight]<=(100))");
        });

        modelBuilder.Entity<EvaluationReport>().ToTable("EvaluationReports", table =>
        {
            table.HasCheckConstraint("CK_EvaluationReports_1", "([Status]='Approved' OR [Status]='TopApproved' OR [Status]='ReturnedForReview' OR [Status]='ManagerRejected' OR [Status]='ManagerApproved' OR [Status]='OfficerSubmitted' OR [Status]='CommitteeApproved' OR [Status]='Draft')");
        });

        modelBuilder.Entity<EvaluationScore>().ToTable("EvaluationScores", table =>
        {
            table.HasCheckConstraint("CK_EvaluationScores_1", "([Score]>=(0) AND [Score]<=(100))");
        });

        modelBuilder.Entity<Invitation>().ToTable("Invitations", table =>
        {
            table.HasCheckConstraint("CK_Invitations_1", "([Status]='Expired' OR [Status]='Declined' OR [Status]='Accepted' OR [Status]='Pending')");
        });

        modelBuilder.Entity<Notification>().ToTable("Notifications");

        modelBuilder.Entity<PurchaseRequest>().ToTable("PurchaseRequests", table =>
        {
            table.HasCheckConstraint("CK_PurchaseRequests_1", "([Priority]='Urgent' OR [Priority]='High' OR [Priority]='Normal' OR [Priority]='Low')");
            table.HasCheckConstraint("CK_PurchaseRequests_2", "([Status]='ConvertedToTender' OR [Status]='Returned' OR [Status]='Rejected' OR [Status]='Approved' OR [Status]='UnderReview' OR [Status]='Submitted' OR [Status]='Draft')");
        });

        modelBuilder.Entity<PurchaseRequestItem>().ToTable("PurchaseRequestItems", table =>
        {
            table.HasCheckConstraint("CK_PurchaseRequestItems_1", "([Quantity]>(0))");
        });

        modelBuilder.Entity<SupplierDocument>().ToTable("SupplierDocuments", table =>
        {
            table.HasCheckConstraint("CK_SupplierDocuments_1", "([FileSizeBytes]>(0))");
        });

        modelBuilder.Entity<SupplierProfile>().ToTable("SupplierProfiles", table =>
        {
            table.HasCheckConstraint("CK_SupplierProfiles_1", "([Status]='Suspended' OR [Status]='ChangesRequested' OR [Status]='Rejected' OR [Status]='Approved' OR [Status]='Pending')");
        });

        modelBuilder.Entity<Tender>().ToTable("Tenders", table =>
        {
            table.HasCheckConstraint("CK_Tenders_1", "([ClosesAt]>[OpensAt])");
            table.HasCheckConstraint("CK_Tenders_2", "([Status]='Awarded' AND [AwardedBidId] IS NOT NULL AND [AwardedAt] IS NOT NULL OR [Status]<>'Awarded' AND [AwardedBidId] IS NULL AND [AwardedAt] IS NULL)");
            table.HasCheckConstraint("CK_Tenders_3", "([Status]='Cancelled' OR [Status]='Awarded' OR [Status]='AwaitingCommitteeDecision' OR [Status]='AwaitingManagerReview' OR [Status]='AwaitingOfficerSummary' OR [Status]='UnderEvaluation' OR [Status]='Closed' OR [Status]='Open' OR [Status]='Published')");
        });

        modelBuilder.Entity<TenderApproval>().ToTable("TenderApprovals", table =>
        {
            table.HasCheckConstraint("CK_TenderApprovals_1", "([Stage]='Top' OR [Stage]='Manager' OR [Stage]='Evaluation')");
            table.HasCheckConstraint("CK_TenderApprovals_2", "([Stage]='Manager' AND [CommitteeAssignmentId] IS NULL OR ([Stage]='Top' OR [Stage]='Evaluation') AND [CommitteeAssignmentId] IS NOT NULL)");
            table.HasCheckConstraint("CK_TenderApprovals_3", "([Stage]='Manager' OR [Decision]='Approved' OR ([Stage]='Top' AND [Decision]='Rejected'))");
            table.HasCheckConstraint("CK_TenderApprovals_4", "([Decision]='ReturnedForReview' OR [Decision]='Rejected' OR [Decision]='Approved')");
        });

        modelBuilder.Entity<TenderDocument>().ToTable("TenderDocuments", table =>
        {
            table.HasCheckConstraint("CK_TenderDocuments_1", "([FileSizeBytes]>(0))");
        });

        // Business records require explicit handling before deletion; Identity retains its standard rules.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => e.ClrType.Namespace == "SourcingTenderManagement.Models.Entities"))
            foreach (var foreignKey in entityType.GetForeignKeys())
                foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
    }
}
