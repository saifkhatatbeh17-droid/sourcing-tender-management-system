using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportsData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerificationNote",
                table: "SupplierDocuments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "SupplierDocuments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "SupplierDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByUserId",
                table: "SupplierDocuments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "dbo",
                columns: table => new
                {
                    AuditEntryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TenderId = table.Column<int>(type: "int", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.AuditEntryId);
                });

            migrationBuilder.CreateTable(
                name: "TenderContracts",
                columns: table => new
                {
                    TenderContractId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GeneratedPdf = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SignedFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SignedStoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SignedContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SignedSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    SignedUploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SignedUploadedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderContracts", x => x.TenderContractId);
                    table.ForeignKey(
                        name: "FK_TenderContracts_Bids_BidId",
                        column: x => x.BidId,
                        principalTable: "Bids",
                        principalColumn: "BidId");
                    table.ForeignKey(
                        name: "FK_TenderContracts_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierDocuments_VerifiedByUserId",
                table: "SupplierDocuments",
                column: "VerifiedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplierDocuments_VerificationStatus",
                table: "SupplierDocuments",
                sql: "[VerificationStatus] IN ('Pending','Verified','Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                schema: "dbo",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                schema: "dbo",
                table: "AuditEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenderId",
                schema: "dbo",
                table: "AuditEntries",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_UserId",
                schema: "dbo",
                table: "AuditEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TenderContracts_BidId",
                table: "TenderContracts",
                column: "BidId");

            migrationBuilder.CreateIndex(
                name: "IX_TenderContracts_TenderId",
                table: "TenderContracts",
                column: "TenderId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierDocuments_AspNetUsers_VerifiedByUserId",
                table: "SupplierDocuments",
                column: "VerifiedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplierDocuments_AspNetUsers_VerifiedByUserId",
                table: "SupplierDocuments");

            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TenderContracts");

            migrationBuilder.DropIndex(
                name: "IX_SupplierDocuments_VerifiedByUserId",
                table: "SupplierDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplierDocuments_VerificationStatus",
                table: "SupplierDocuments");

            migrationBuilder.DropColumn(
                name: "VerificationNote",
                table: "SupplierDocuments");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "SupplierDocuments");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "SupplierDocuments");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "SupplierDocuments");
        }
    }
}
