using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTopCommitteeEditCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TopEditManagerNotes",
                table: "Tenders",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopEditOfficerNotes",
                table: "Tenders",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TopEditOfficerRespondedAt",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopEditRequestNotes",
                table: "Tenders",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TopEditRequestedAt",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopEditRequestedByUserId",
                table: "Tenders",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopEditState",
                table: "Tenders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TopEditManagerNotes",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "TopEditOfficerNotes",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "TopEditOfficerRespondedAt",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "TopEditRequestNotes",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "TopEditRequestedAt",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "TopEditRequestedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "TopEditState",
                table: "Tenders");
        }
    }
}
