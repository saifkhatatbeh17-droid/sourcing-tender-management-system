using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTenderDraftStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenders_3",
                table: "Tenders");

            // Move existing tenders into the live flow before disallowing new drafts.
            migrationBuilder.Sql("UPDATE dbo.Tenders SET Status = CASE WHEN ClosesAt <= SYSUTCDATETIME() THEN 'Closed' ELSE 'Published' END WHERE Status = 'Draft';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenders_3",
                table: "Tenders",
                sql: "([Status]='Cancelled' OR [Status]='Awarded' OR [Status]='AwaitingCommitteeDecision' OR [Status]='AwaitingManagerReview' OR [Status]='AwaitingOfficerSummary' OR [Status]='UnderEvaluation' OR [Status]='Closed' OR [Status]='Open' OR [Status]='Published')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenders_3",
                table: "Tenders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenders_3",
                table: "Tenders",
                sql: "([Status]='Cancelled' OR [Status]='Awarded' OR [Status]='AwaitingCommitteeDecision' OR [Status]='AwaitingManagerReview' OR [Status]='AwaitingOfficerSummary' OR [Status]='UnderEvaluation' OR [Status]='Closed' OR [Status]='Open' OR [Status]='Published' OR [Status]='Draft')");
        }
    }
}
