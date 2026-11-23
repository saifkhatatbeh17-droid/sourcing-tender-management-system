using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemLegalWarranty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LegalWarrantyMonths",
                table: "PurchaseRequestItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequestItems_LegalWarranty",
                table: "PurchaseRequestItems",
                sql: "[LegalWarrantyMonths] IS NULL OR ([LegalWarrantyMonths]>=1 AND [LegalWarrantyMonths]<=120)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequestItems_LegalWarranty",
                table: "PurchaseRequestItems");

            migrationBuilder.DropColumn(
                name: "LegalWarrantyMonths",
                table: "PurchaseRequestItems");
        }
    }
}
