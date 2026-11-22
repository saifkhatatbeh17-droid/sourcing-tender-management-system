using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiInteractionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiInteractions",
                schema: "dbo",
                columns: table => new
                {
                    AiInteractionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Workspace = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Feature = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SchemaName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ErrorType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SchemaConforms = table.Column<bool>(type: "bit", nullable: true),
                    SchemaIssues = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    InputChars = table.Column<int>(type: "int", nullable: false),
                    InputSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutputChars = table.Column<int>(type: "int", nullable: true),
                    OutputPreview = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsInteractive = table.Column<bool>(type: "bit", nullable: false),
                    RecordType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RecordId = table.Column<int>(type: "int", nullable: true),
                    UserFeedback = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiInteractions", x => x.AiInteractionId);
                    table.CheckConstraint("CK_AiInteractions_Outcome", "[Outcome] IN ('Succeeded', 'Failed', 'Cancelled', 'LimitReached')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiInteractions_Feature_CreatedAtUtc",
                schema: "dbo",
                table: "AiInteractions",
                columns: new[] { "Feature", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiInteractions_UserId_IsInteractive_CreatedAtUtc",
                schema: "dbo",
                table: "AiInteractions",
                columns: new[] { "UserId", "IsInteractive", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiInteractions",
                schema: "dbo");
        }
    }
}
