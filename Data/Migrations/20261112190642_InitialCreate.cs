using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    DepartmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.DepartmentId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RelativeUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequests",
                columns: table => new
                {
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    ReviewNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequests", x => x.PurchaseRequestId);
                    table.CheckConstraint("CK_PurchaseRequests_1", "([Priority]='Urgent' OR [Priority]='High' OR [Priority]='Normal' OR [Priority]='Low')");
                    table.CheckConstraint("CK_PurchaseRequests_2", "([Status]='ConvertedToTender' OR [Status]='Returned' OR [Status]='Rejected' OR [Status]='Approved' OR [Status]='UnderReview' OR [Status]='Submitted' OR [Status]='Draft')");
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId");
                });

            migrationBuilder.CreateTable(
                name: "SupplierProfiles",
                columns: table => new
                {
                    SupplierProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TaxNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    ReviewNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierProfiles", x => x.SupplierProfileId);
                    table.CheckConstraint("CK_SupplierProfiles_1", "([Status]='Suspended' OR [Status]='ChangesRequested' OR [Status]='Rejected' OR [Status]='Approved' OR [Status]='Pending')");
                    table.ForeignKey(
                        name: "FK_SupplierProfiles_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupplierProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequestItems",
                columns: table => new
                {
                    PurchaseRequestItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Specifications = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequestItems", x => x.PurchaseRequestItemId);
                    table.CheckConstraint("CK_PurchaseRequestItems_1", "([Quantity]>(0))");
                    table.ForeignKey(
                        name: "FK_PurchaseRequestItems_PurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "PurchaseRequestId");
                });

            migrationBuilder.CreateTable(
                name: "SupplierDocuments",
                columns: table => new
                {
                    SupplierDocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierProfileId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierDocuments", x => x.SupplierDocumentId);
                    table.CheckConstraint("CK_SupplierDocuments_1", "([FileSizeBytes]>(0))");
                    table.ForeignKey(
                        name: "FK_SupplierDocuments_SupplierProfiles_SupplierProfileId",
                        column: x => x.SupplierProfileId,
                        principalTable: "SupplierProfiles",
                        principalColumn: "SupplierProfileId");
                });

            migrationBuilder.CreateTable(
                name: "AiAnalyses",
                columns: table => new
                {
                    AiAnalysisId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FinancialWeight = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    RequirementsWeight = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAnalyses", x => x.AiAnalysisId);
                    table.CheckConstraint("CK_AiAnalyses_1", "(isjson([ResultJson])=(1))");
                    table.CheckConstraint("CK_AiAnalyses_2", "([FinancialWeight]=(60) AND [RequirementsWeight]=(40) OR [FinancialWeight]=(100) AND [RequirementsWeight]=(0) OR [FinancialWeight]=(0) AND [RequirementsWeight]=(100))");
                    table.ForeignKey(
                        name: "FK_AiAnalyses_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BidDocuments",
                columns: table => new
                {
                    BidDocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidDocuments", x => x.BidDocumentId);
                    table.CheckConstraint("CK_BidDocuments_1", "([FileSizeBytes]>(0))");
                });

            migrationBuilder.CreateTable(
                name: "BidItems",
                columns: table => new
                {
                    BidItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    PurchaseRequestItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OfferedSpecifications = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidItems", x => x.BidItemId);
                    table.CheckConstraint("CK_BidItems_1", "([Quantity]>(0))");
                    table.CheckConstraint("CK_BidItems_2", "([UnitPrice]>=(0))");
                    table.ForeignKey(
                        name: "FK_BidItems_PurchaseRequestItems_PurchaseRequestItemId",
                        column: x => x.PurchaseRequestItemId,
                        principalTable: "PurchaseRequestItems",
                        principalColumn: "PurchaseRequestItemId");
                });

            migrationBuilder.CreateTable(
                name: "Bids",
                columns: table => new
                {
                    BidId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvitationId = table.Column<int>(type: "int", nullable: false),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    DeliveryDays = table.Column<int>(type: "int", nullable: false),
                    PaymentTerms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    TechnicalDetails = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdditionalCosts = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    ComplianceStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ComplianceNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ComplianceReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ComplianceReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bids", x => x.BidId);
                    table.UniqueConstraint("AK_Bids_TenderId_BidId", x => new { x.TenderId, x.BidId });
                    table.CheckConstraint("CK_Bids_1", "([AdditionalCosts]>=(0))");
                    table.CheckConstraint("CK_Bids_2", "([ComplianceStatus]='PendingClarification' OR [ComplianceStatus]='NonCompliant' OR [ComplianceStatus]='Compliant')");
                    table.CheckConstraint("CK_Bids_3", "([DeliveryDays]>=(0))");
                    table.CheckConstraint("CK_Bids_4", "([Status]='Withdrawn' OR [Status]='Rejected' OR [Status]='Accepted' OR [Status]='NonCompliant' OR [Status]='Compliant' OR [Status]='UnderReview' OR [Status]='Submitted' OR [Status]='Draft')");
                    table.CheckConstraint("CK_Bids_5", "([TaxAmount]>=(0))");
                    table.ForeignKey(
                        name: "FK_Bids_AspNetUsers_ComplianceReviewedByUserId",
                        column: x => x.ComplianceReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Tenders",
                columns: table => new
                {
                    TenderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Terms = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OpensAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosesAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "Draft"),
                    AwardedBidId = table.Column<int>(type: "int", nullable: true),
                    AwardNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AwardedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenders", x => x.TenderId);
                    table.CheckConstraint("CK_Tenders_1", "([ClosesAt]>[OpensAt])");
                    table.CheckConstraint("CK_Tenders_2", "([Status]='Awarded' AND [AwardedBidId] IS NOT NULL AND [AwardedAt] IS NOT NULL OR [Status]<>'Awarded' AND [AwardedBidId] IS NULL AND [AwardedAt] IS NULL)");
                    table.CheckConstraint("CK_Tenders_3", "([Status]='Cancelled' OR [Status]='Awarded' OR [Status]='AwaitingCommitteeDecision' OR [Status]='AwaitingManagerReview' OR [Status]='AwaitingOfficerSummary' OR [Status]='UnderEvaluation' OR [Status]='Closed' OR [Status]='Open' OR [Status]='Published' OR [Status]='Draft')");
                    table.ForeignKey(
                        name: "FK_Tenders_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tenders_Bids_TenderId_AwardedBidId",
                        columns: x => new { x.TenderId, x.AwardedBidId },
                        principalTable: "Bids",
                        principalColumns: new[] { "TenderId", "BidId" });
                    table.ForeignKey(
                        name: "FK_Tenders_PurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "PurchaseRequestId");
                });

            migrationBuilder.CreateTable(
                name: "Clarifications",
                columns: table => new
                {
                    ClarificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SenderUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RecipientUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: true),
                    TenderId = table.Column<int>(type: "int", nullable: true),
                    BidId = table.Column<int>(type: "int", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clarifications", x => x.ClarificationId);
                    table.CheckConstraint("CK_Clarifications_1", "([Status]='Resolved' OR [Status]='Answered' OR [Status]='Pending')");
                    table.CheckConstraint("CK_Clarifications_2", "([SenderUserId]<>[RecipientUserId])");
                    table.CheckConstraint("CK_Clarifications_3", "(((case when [PurchaseRequestId] IS NULL then (0) else (1) end+case when [TenderId] IS NULL then (0) else (1) end)+case when [BidId] IS NULL then (0) else (1) end)=(1))");
                    table.ForeignKey(
                        name: "FK_Clarifications_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Clarifications_AspNetUsers_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Clarifications_Bids_BidId",
                        column: x => x.BidId,
                        principalTable: "Bids",
                        principalColumn: "BidId");
                    table.ForeignKey(
                        name: "FK_Clarifications_PurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "PurchaseRequestId");
                    table.ForeignKey(
                        name: "FK_Clarifications_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateTable(
                name: "CommitteeAssignments",
                columns: table => new
                {
                    CommitteeAssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CommitteeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeAssignments", x => x.CommitteeAssignmentId);
                    table.UniqueConstraint("AK_CommitteeAssignments_TenderId_CommitteeAssignmentId", x => new { x.TenderId, x.CommitteeAssignmentId });
                    table.UniqueConstraint("AK_CommitteeAssignments_TenderId_CommitteeAssignmentId_UserId_CommitteeType", x => new { x.TenderId, x.CommitteeAssignmentId, x.UserId, x.CommitteeType });
                    table.CheckConstraint("CK_CommitteeAssignments_1", "([CommitteeType]='Top' OR [CommitteeType]='Evaluation')");
                    table.CheckConstraint("CK_CommitteeAssignments_2", "([Position]>=(1) AND [Position]<=(4))");
                    table.ForeignKey(
                        name: "FK_CommitteeAssignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommitteeAssignments_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateTable(
                name: "EvaluationCriteria",
                columns: table => new
                {
                    EvaluationCriterionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCriteria", x => x.EvaluationCriterionId);
                    table.UniqueConstraint("AK_EvaluationCriteria_TenderId_EvaluationCriterionId", x => new { x.TenderId, x.EvaluationCriterionId });
                    table.CheckConstraint("CK_EvaluationCriteria_1", "([Category]='Requirements' OR [Category]='Financial')");
                    table.CheckConstraint("CK_EvaluationCriteria_2", "([Weight]>(0) AND [Weight]<=(100))");
                    table.ForeignKey(
                        name: "FK_EvaluationCriteria_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateTable(
                name: "EvaluationReports",
                columns: table => new
                {
                    EvaluationReportId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    RecommendedBidId = table.Column<int>(type: "int", nullable: true),
                    CommitteeSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OfficerSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreparedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManagerSummary = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationReports", x => x.EvaluationReportId);
                    table.CheckConstraint("CK_EvaluationReports_1", "([Status]='Approved' OR [Status]='TopApproved' OR [Status]='ReturnedForReview' OR [Status]='ManagerRejected' OR [Status]='ManagerApproved' OR [Status]='OfficerSubmitted' OR [Status]='CommitteeApproved' OR [Status]='Draft')");
                    table.ForeignKey(
                        name: "FK_EvaluationReports_AspNetUsers_PreparedByUserId",
                        column: x => x.PreparedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EvaluationReports_Bids_TenderId_RecommendedBidId",
                        columns: x => new { x.TenderId, x.RecommendedBidId },
                        principalTable: "Bids",
                        principalColumns: new[] { "TenderId", "BidId" });
                    table.ForeignKey(
                        name: "FK_EvaluationReports_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    InvitationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    SupplierProfileId = table.Column<int>(type: "int", nullable: false),
                    InvitedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.InvitationId);
                    table.UniqueConstraint("AK_Invitations_InvitationId_TenderId", x => new { x.InvitationId, x.TenderId });
                    table.CheckConstraint("CK_Invitations_1", "([Status]='Expired' OR [Status]='Declined' OR [Status]='Accepted' OR [Status]='Pending')");
                    table.ForeignKey(
                        name: "FK_Invitations_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invitations_SupplierProfiles_SupplierProfileId",
                        column: x => x.SupplierProfileId,
                        principalTable: "SupplierProfiles",
                        principalColumn: "SupplierProfileId");
                    table.ForeignKey(
                        name: "FK_Invitations_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateTable(
                name: "TenderDocuments",
                columns: table => new
                {
                    TenderDocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderDocuments", x => x.TenderDocumentId);
                    table.CheckConstraint("CK_TenderDocuments_1", "([FileSizeBytes]>(0))");
                    table.ForeignKey(
                        name: "FK_TenderDocuments_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateTable(
                name: "ClarificationResponses",
                columns: table => new
                {
                    ClarificationResponseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClarificationId = table.Column<int>(type: "int", nullable: false),
                    RespondedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClarificationResponses", x => x.ClarificationResponseId);
                    table.ForeignKey(
                        name: "FK_ClarificationResponses_AspNetUsers_RespondedByUserId",
                        column: x => x.RespondedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClarificationResponses_Clarifications_ClarificationId",
                        column: x => x.ClarificationId,
                        principalTable: "Clarifications",
                        principalColumn: "ClarificationId");
                });

            migrationBuilder.CreateTable(
                name: "Evaluations",
                columns: table => new
                {
                    EvaluationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    CommitteeAssignmentId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluations", x => x.EvaluationId);
                    table.UniqueConstraint("AK_Evaluations_TenderId_EvaluationId", x => new { x.TenderId, x.EvaluationId });
                    table.CheckConstraint("CK_Evaluations_1", "([Status]='Approved' OR [Status]='Submitted' OR [Status]='Draft')");
                    table.ForeignKey(
                        name: "FK_Evaluations_Bids_TenderId_BidId",
                        columns: x => new { x.TenderId, x.BidId },
                        principalTable: "Bids",
                        principalColumns: new[] { "TenderId", "BidId" });
                    table.ForeignKey(
                        name: "FK_Evaluations_CommitteeAssignments_TenderId_CommitteeAssignmentId",
                        columns: x => new { x.TenderId, x.CommitteeAssignmentId },
                        principalTable: "CommitteeAssignments",
                        principalColumns: new[] { "TenderId", "CommitteeAssignmentId" });
                });

            migrationBuilder.CreateTable(
                name: "TenderApprovals",
                columns: table => new
                {
                    TenderApprovalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CommitteeAssignmentId = table.Column<int>(type: "int", nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderApprovals", x => x.TenderApprovalId);
                    table.CheckConstraint("CK_TenderApprovals_1", "([Stage]='Top' OR [Stage]='Manager' OR [Stage]='Evaluation')");
                    table.CheckConstraint("CK_TenderApprovals_2", "([Stage]='Manager' AND [CommitteeAssignmentId] IS NULL OR ([Stage]='Top' OR [Stage]='Evaluation') AND [CommitteeAssignmentId] IS NOT NULL)");
                    table.CheckConstraint("CK_TenderApprovals_3", "([Stage]='Manager' OR [Decision]='Approved')");
                    table.CheckConstraint("CK_TenderApprovals_4", "([Decision]='ReturnedForReview' OR [Decision]='Rejected' OR [Decision]='Approved')");
                    table.ForeignKey(
                        name: "FK_TenderApprovals_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TenderApprovals_CommitteeAssignments_TenderId_CommitteeAssignmentId",
                        columns: x => new { x.TenderId, x.CommitteeAssignmentId },
                        principalTable: "CommitteeAssignments",
                        principalColumns: new[] { "TenderId", "CommitteeAssignmentId" });
                    table.ForeignKey(
                        name: "FK_TenderApprovals_CommitteeAssignments_TenderId_CommitteeAssignmentId_UserId_Stage",
                        columns: x => new { x.TenderId, x.CommitteeAssignmentId, x.UserId, x.Stage },
                        principalTable: "CommitteeAssignments",
                        principalColumns: new[] { "TenderId", "CommitteeAssignmentId", "UserId", "CommitteeType" });
                    table.ForeignKey(
                        name: "FK_TenderApprovals_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateTable(
                name: "EvaluationScores",
                columns: table => new
                {
                    EvaluationScoreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    EvaluationId = table.Column<int>(type: "int", nullable: false),
                    EvaluationCriterionId = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationScores", x => x.EvaluationScoreId);
                    table.CheckConstraint("CK_EvaluationScores_1", "([Score]>=(0) AND [Score]<=(100))");
                    table.ForeignKey(
                        name: "FK_EvaluationScores_EvaluationCriteria_TenderId_EvaluationCriterionId",
                        columns: x => new { x.TenderId, x.EvaluationCriterionId },
                        principalTable: "EvaluationCriteria",
                        principalColumns: new[] { "TenderId", "EvaluationCriterionId" });
                    table.ForeignKey(
                        name: "FK_EvaluationScores_Evaluations_TenderId_EvaluationId",
                        columns: x => new { x.TenderId, x.EvaluationId },
                        principalTable: "Evaluations",
                        principalColumns: new[] { "TenderId", "EvaluationId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalyses_RequestedByUserId",
                table: "AiAnalyses",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalyses_TenderId",
                table: "AiAnalyses",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DepartmentId",
                table: "AspNetUsers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BidDocuments_BidId",
                table: "BidDocuments",
                column: "BidId");

            migrationBuilder.CreateIndex(
                name: "IX_BidItems_BidId_PurchaseRequestItemId",
                table: "BidItems",
                columns: new[] { "BidId", "PurchaseRequestItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BidItems_PurchaseRequestItemId",
                table: "BidItems",
                column: "PurchaseRequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_ComplianceReviewedByUserId",
                table: "Bids",
                column: "ComplianceReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_InvitationId",
                table: "Bids",
                column: "InvitationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bids_InvitationId_TenderId",
                table: "Bids",
                columns: new[] { "InvitationId", "TenderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bids_TenderId_BidId",
                table: "Bids",
                columns: new[] { "TenderId", "BidId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationResponses_ClarificationId",
                table: "ClarificationResponses",
                column: "ClarificationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationResponses_RespondedByUserId",
                table: "ClarificationResponses",
                column: "RespondedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_BidId",
                table: "Clarifications",
                column: "BidId");

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_PurchaseRequestId",
                table: "Clarifications",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_RecipientUserId_Status",
                table: "Clarifications",
                columns: new[] { "RecipientUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_SenderUserId",
                table: "Clarifications",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_TenderId",
                table: "Clarifications",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeAssignments_TenderId_CommitteeAssignmentId",
                table: "CommitteeAssignments",
                columns: new[] { "TenderId", "CommitteeAssignmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeAssignments_TenderId_CommitteeAssignmentId_UserId_CommitteeType",
                table: "CommitteeAssignments",
                columns: new[] { "TenderId", "CommitteeAssignmentId", "UserId", "CommitteeType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeAssignments_TenderId_CommitteeType_Position",
                table: "CommitteeAssignments",
                columns: new[] { "TenderId", "CommitteeType", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeAssignments_TenderId_CommitteeType_UserId",
                table: "CommitteeAssignments",
                columns: new[] { "TenderId", "CommitteeType", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeAssignments_UserId",
                table: "CommitteeAssignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCriteria_TenderId_EvaluationCriterionId",
                table: "EvaluationCriteria",
                columns: new[] { "TenderId", "EvaluationCriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCriteria_TenderId_Name",
                table: "EvaluationCriteria",
                columns: new[] { "TenderId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationReports_PreparedByUserId",
                table: "EvaluationReports",
                column: "PreparedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationReports_TenderId",
                table: "EvaluationReports",
                column: "TenderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationReports_TenderId_RecommendedBidId",
                table: "EvaluationReports",
                columns: new[] { "TenderId", "RecommendedBidId" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_BidId_CommitteeAssignmentId",
                table: "Evaluations",
                columns: new[] { "BidId", "CommitteeAssignmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_TenderId_BidId",
                table: "Evaluations",
                columns: new[] { "TenderId", "BidId" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_TenderId_CommitteeAssignmentId",
                table: "Evaluations",
                columns: new[] { "TenderId", "CommitteeAssignmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_TenderId_EvaluationId",
                table: "Evaluations",
                columns: new[] { "TenderId", "EvaluationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_EvaluationId_EvaluationCriterionId",
                table: "EvaluationScores",
                columns: new[] { "EvaluationId", "EvaluationCriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_TenderId_EvaluationCriterionId",
                table: "EvaluationScores",
                columns: new[] { "TenderId", "EvaluationCriterionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_TenderId_EvaluationId",
                table: "EvaluationScores",
                columns: new[] { "TenderId", "EvaluationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_InvitationId_TenderId",
                table: "Invitations",
                columns: new[] { "InvitationId", "TenderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_InvitedByUserId",
                table: "Invitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_SupplierProfileId",
                table: "Invitations",
                column: "SupplierProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TenderId_SupplierProfileId",
                table: "Invitations",
                columns: new[] { "TenderId", "SupplierProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestItems_PurchaseRequestId",
                table: "PurchaseRequestItems",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_DepartmentId",
                table: "PurchaseRequests",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_RequestedByUserId",
                table: "PurchaseRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_ReviewedByUserId",
                table: "PurchaseRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDocuments_SupplierProfileId",
                table: "SupplierDocuments",
                column: "SupplierProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProfiles_RegistrationNumber",
                table: "SupplierProfiles",
                column: "RegistrationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProfiles_ReviewedByUserId",
                table: "SupplierProfiles",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProfiles_UserId",
                table: "SupplierProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderApprovals_CommitteeAssignmentId",
                table: "TenderApprovals",
                column: "CommitteeAssignmentId",
                unique: true,
                filter: "([CommitteeAssignmentId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_TenderApprovals_TenderId_CommitteeAssignmentId_UserId_Stage",
                table: "TenderApprovals",
                columns: new[] { "TenderId", "CommitteeAssignmentId", "UserId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_TenderApprovals_TenderId_Stage_UserId",
                table: "TenderApprovals",
                columns: new[] { "TenderId", "Stage", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderApprovals_UserId",
                table: "TenderApprovals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TenderDocuments_TenderId",
                table: "TenderDocuments",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_CreatedByUserId",
                table: "Tenders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_PurchaseRequestId",
                table: "Tenders",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_TenderId_AwardedBidId",
                table: "Tenders",
                columns: new[] { "TenderId", "AwardedBidId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AiAnalyses_Tenders_TenderId",
                table: "AiAnalyses",
                column: "TenderId",
                principalTable: "Tenders",
                principalColumn: "TenderId");

            migrationBuilder.AddForeignKey(
                name: "FK_BidDocuments_Bids_BidId",
                table: "BidDocuments",
                column: "BidId",
                principalTable: "Bids",
                principalColumn: "BidId");

            migrationBuilder.AddForeignKey(
                name: "FK_BidItems_Bids_BidId",
                table: "BidItems",
                column: "BidId",
                principalTable: "Bids",
                principalColumn: "BidId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bids_Invitations_InvitationId_TenderId",
                table: "Bids",
                columns: new[] { "InvitationId", "TenderId" },
                principalTable: "Invitations",
                principalColumns: new[] { "InvitationId", "TenderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bids_AspNetUsers_ComplianceReviewedByUserId",
                table: "Bids");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_AspNetUsers_InvitedByUserId",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequests_AspNetUsers_RequestedByUserId",
                table: "PurchaseRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequests_AspNetUsers_ReviewedByUserId",
                table: "PurchaseRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierProfiles_AspNetUsers_ReviewedByUserId",
                table: "SupplierProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierProfiles_AspNetUsers_UserId",
                table: "SupplierProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_AspNetUsers_CreatedByUserId",
                table: "Tenders");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Tenders_TenderId",
                table: "Invitations");

            migrationBuilder.DropTable(
                name: "AiAnalyses");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "BidDocuments");

            migrationBuilder.DropTable(
                name: "BidItems");

            migrationBuilder.DropTable(
                name: "ClarificationResponses");

            migrationBuilder.DropTable(
                name: "EvaluationReports");

            migrationBuilder.DropTable(
                name: "EvaluationScores");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "SupplierDocuments");

            migrationBuilder.DropTable(
                name: "TenderApprovals");

            migrationBuilder.DropTable(
                name: "TenderDocuments");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "PurchaseRequestItems");

            migrationBuilder.DropTable(
                name: "Clarifications");

            migrationBuilder.DropTable(
                name: "EvaluationCriteria");

            migrationBuilder.DropTable(
                name: "Evaluations");

            migrationBuilder.DropTable(
                name: "CommitteeAssignments");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Tenders");

            migrationBuilder.DropTable(
                name: "Bids");

            migrationBuilder.DropTable(
                name: "PurchaseRequests");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "SupplierProfiles");
        }
    }
}
