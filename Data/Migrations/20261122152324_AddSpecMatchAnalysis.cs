using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecMatchAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpecMatchAnalyses",
                schema: "dbo",
                columns: table => new
                {
                    SpecMatchAnalysisId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AiAvailable = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecMatchAnalyses", x => x.SpecMatchAnalysisId);
                    table.ForeignKey(
                        name: "FK_SpecMatchAnalyses_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SpecMatchAnalyses_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpecMatchAnalyses_CreatedByUserId",
                schema: "dbo",
                table: "SpecMatchAnalyses",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SpecMatchAnalyses_TenderId_SpecMatchAnalysisId",
                schema: "dbo",
                table: "SpecMatchAnalyses",
                columns: new[] { "TenderId", "SpecMatchAnalysisId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpecMatchAnalyses",
                schema: "dbo");
        }
    }
}
