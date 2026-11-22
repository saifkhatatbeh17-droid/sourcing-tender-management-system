using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBidGuarantees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BidGuarantees",
                columns: table => new
                {
                    BidGuaranteeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Coverage = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    VerificationToken = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidGuarantees", x => x.BidGuaranteeId);
                    table.CheckConstraint("CK_BidGuarantees_Coverage", "[Coverage] IN ('Product', 'Service')");
                    table.CheckConstraint("CK_BidGuarantees_Dates", "[ExpiresOn] >= [StartsOn]");
                    table.CheckConstraint("CK_BidGuarantees_Kind", "[Kind] IN ('Legal', 'Bank')");
                    table.ForeignKey(
                        name: "FK_BidGuarantees_Bids_BidId",
                        column: x => x.BidId,
                        principalTable: "Bids",
                        principalColumn: "BidId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BidGuarantees_BidId_Kind",
                table: "BidGuarantees",
                columns: new[] { "BidId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BidGuarantees_VerificationToken",
                table: "BidGuarantees",
                column: "VerificationToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BidGuarantees");
        }
    }
}
