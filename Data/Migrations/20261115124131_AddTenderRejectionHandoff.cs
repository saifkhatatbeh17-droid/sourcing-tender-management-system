using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenderRejectionHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectionForwardedAt",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionForwardedByUserId",
                table: "Tenders",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacesTenderId",
                table: "Tenders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_RejectionForwardedByUserId",
                table: "Tenders",
                column: "RejectionForwardedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_ReplacesTenderId",
                table: "Tenders",
                column: "ReplacesTenderId",
                unique: true,
                filter: "[ReplacesTenderId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenders_RejectionForward",
                table: "Tenders",
                sql: "([RejectionForwardedAt] IS NULL AND [RejectionForwardedByUserId] IS NULL) OR ([RejectionForwardedAt] IS NOT NULL AND [RejectionForwardedByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenders_Replacement",
                table: "Tenders",
                sql: "[ReplacesTenderId] IS NULL OR [ReplacesTenderId] <> [TenderId]");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_AspNetUsers_RejectionForwardedByUserId",
                table: "Tenders",
                column: "RejectionForwardedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_Tenders_ReplacesTenderId",
                table: "Tenders",
                column: "ReplacesTenderId",
                principalTable: "Tenders",
                principalColumn: "TenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_AspNetUsers_RejectionForwardedByUserId",
                table: "Tenders");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_Tenders_ReplacesTenderId",
                table: "Tenders");

            migrationBuilder.DropIndex(
                name: "IX_Tenders_RejectionForwardedByUserId",
                table: "Tenders");

            migrationBuilder.DropIndex(
                name: "IX_Tenders_ReplacesTenderId",
                table: "Tenders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenders_RejectionForward",
                table: "Tenders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenders_Replacement",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "RejectionForwardedAt",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "RejectionForwardedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ReplacesTenderId",
                table: "Tenders");
        }
    }
}
