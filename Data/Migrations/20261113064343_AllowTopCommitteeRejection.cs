using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowTopCommitteeRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TenderApprovals_3",
                table: "TenderApprovals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenderApprovals_3",
                table: "TenderApprovals",
                sql: "([Stage]='Manager' OR [Decision]='Approved' OR ([Stage]='Top' AND [Decision]='Rejected'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TenderApprovals_3",
                table: "TenderApprovals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenderApprovals_3",
                table: "TenderApprovals",
                sql: "([Stage]='Manager' OR [Decision]='Approved')");
        }
    }
}
