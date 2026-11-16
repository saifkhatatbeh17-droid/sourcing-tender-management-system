using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowCustomAiWeights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiAnalyses_2",
                table: "AiAnalyses");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiAnalyses_2",
                table: "AiAnalyses",
                sql: "([FinancialWeight]>=0 AND [FinancialWeight]<=100 AND [RequirementsWeight]>=0 AND [RequirementsWeight]<=100 AND [FinancialWeight]+[RequirementsWeight]=100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiAnalyses_2",
                table: "AiAnalyses");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiAnalyses_2",
                table: "AiAnalyses",
                sql: "([FinancialWeight]=(60) AND [RequirementsWeight]=(40) OR [FinancialWeight]=(100) AND [RequirementsWeight]=(0) OR [FinancialWeight]=(0) AND [RequirementsWeight]=(100))");
        }
    }
}
