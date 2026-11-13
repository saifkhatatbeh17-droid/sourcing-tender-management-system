using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFaceCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "UserFaceCredentials",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProtectedTemplates = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    TemplateFormatVersion = table.Column<short>(type: "smallint", nullable: true),
                    ModelIdentifier = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SampleCount = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    EnrollmentPromptedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ConsentVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ConsentGrantedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    EnrolledAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFaceCredentials", x => x.UserId);
                    table.CheckConstraint("CK_UserFaceCredentials_EnrollmentState", "(\n    [SampleCount] = 0\n    AND [ProtectedTemplates] IS NULL\n    AND [TemplateFormatVersion] IS NULL\n    AND [ModelIdentifier] IS NULL\n    AND [ConsentVersion] IS NULL\n    AND [ConsentGrantedAt] IS NULL\n    AND [EnrolledAt] IS NULL\n)\nOR\n(\n    [SampleCount] BETWEEN 3 AND 5\n    AND [ProtectedTemplates] IS NOT NULL\n    AND DATALENGTH([ProtectedTemplates]) > 0\n    AND [TemplateFormatVersion] IS NOT NULL\n    AND [TemplateFormatVersion] > 0\n    AND [ModelIdentifier] IS NOT NULL\n    AND LEN([ModelIdentifier]) > 0\n    AND [ConsentVersion] IS NOT NULL\n    AND LEN([ConsentVersion]) > 0\n    AND [ConsentGrantedAt] IS NOT NULL\n    AND [EnrolledAt] IS NOT NULL\n)");
                    table.ForeignKey(
                        name: "FK_UserFaceCredentials_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserFaceCredentials",
                schema: "dbo");
        }
    }
}
