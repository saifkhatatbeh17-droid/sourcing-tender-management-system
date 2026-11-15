using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommitteeConsensusAndOfferScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommitteeFinancialWeight",
                table: "Tenders",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CommitteeWeightsLockedAt",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinancialMark",
                table: "Evaluations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TechnicalMark",
                table: "Evaluations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommitteeTieVotes",
                columns: table => new
                {
                    CommitteeTieVoteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    CommitteeAssignmentId = table.Column<int>(type: "int", nullable: false),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeTieVotes", x => x.CommitteeTieVoteId);
                    table.ForeignKey(
                        name: "FK_CommitteeTieVotes_Bids_TenderId_BidId",
                        columns: x => new { x.TenderId, x.BidId },
                        principalTable: "Bids",
                        principalColumns: new[] { "TenderId", "BidId" });
                    table.ForeignKey(
                        name: "FK_CommitteeTieVotes_CommitteeAssignments_TenderId_CommitteeAssignmentId",
                        columns: x => new { x.TenderId, x.CommitteeAssignmentId },
                        principalTable: "CommitteeAssignments",
                        principalColumns: new[] { "TenderId", "CommitteeAssignmentId" });
                    table.ForeignKey(
                        name: "FK_CommitteeTieVotes_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateTable(
                name: "CommitteeWeightProposals",
                columns: table => new
                {
                    CommitteeWeightProposalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    CommitteeAssignmentId = table.Column<int>(type: "int", nullable: false),
                    FinancialWeight = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeWeightProposals", x => x.CommitteeWeightProposalId);
                    table.CheckConstraint("CK_CommitteeWeightProposals_Weight", "[FinancialWeight] > 0 AND [FinancialWeight] < 100");
                    table.ForeignKey(
                        name: "FK_CommitteeWeightProposals_CommitteeAssignments_TenderId_CommitteeAssignmentId",
                        columns: x => new { x.TenderId, x.CommitteeAssignmentId },
                        principalTable: "CommitteeAssignments",
                        principalColumns: new[] { "TenderId", "CommitteeAssignmentId" });
                    table.ForeignKey(
                        name: "FK_CommitteeWeightProposals_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenders_CommitteeWeightLock",
                table: "Tenders",
                sql: "([CommitteeFinancialWeight] IS NULL AND [CommitteeWeightsLockedAt] IS NULL) OR ([CommitteeFinancialWeight] IS NOT NULL AND [CommitteeFinancialWeight] > 0 AND [CommitteeFinancialWeight] < 100 AND [CommitteeWeightsLockedAt] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Evaluations_DirectMarks",
                table: "Evaluations",
                sql: "([FinancialMark] IS NULL AND [TechnicalMark] IS NULL) OR ([FinancialMark] IS NOT NULL AND [TechnicalMark] IS NOT NULL AND [FinancialMark] >= 0 AND [TechnicalMark] >= 0 AND [FinancialMark] + [TechnicalMark] <= 100)");

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeTieVotes_TenderId_BidId",
                table: "CommitteeTieVotes",
                columns: new[] { "TenderId", "BidId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeTieVotes_TenderId_CommitteeAssignmentId",
                table: "CommitteeTieVotes",
                columns: new[] { "TenderId", "CommitteeAssignmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeWeightProposals_TenderId_CommitteeAssignmentId",
                table: "CommitteeWeightProposals",
                columns: new[] { "TenderId", "CommitteeAssignmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommitteeTieVotes");

            migrationBuilder.DropTable(
                name: "CommitteeWeightProposals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenders_CommitteeWeightLock",
                table: "Tenders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Evaluations_DirectMarks",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "CommitteeFinancialWeight",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CommitteeWeightsLockedAt",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "FinancialMark",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "TechnicalMark",
                table: "Evaluations");
        }
    }
}
