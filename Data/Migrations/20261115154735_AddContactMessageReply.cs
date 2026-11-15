using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContactMessageReply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ContactMessages_Status",
                table: "ContactMessages");

            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt",
                table: "ContactMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepliedByUserId",
                table: "ContactMessages",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplyAttemptId",
                table: "ContactMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyText",
                table: "ContactMessages",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactMessages_RepliedByUserId",
                table: "ContactMessages",
                column: "RepliedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContactMessages_Status",
                table: "ContactMessages",
                sql: "([Status]='Unread' OR [Status]='Read' OR [Status]='Replied')");

            migrationBuilder.AddForeignKey(
                name: "FK_ContactMessages_AspNetUsers_RepliedByUserId",
                table: "ContactMessages",
                column: "RepliedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContactMessages_AspNetUsers_RepliedByUserId",
                table: "ContactMessages");

            migrationBuilder.DropIndex(
                name: "IX_ContactMessages_RepliedByUserId",
                table: "ContactMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ContactMessages_Status",
                table: "ContactMessages");

            migrationBuilder.DropColumn(
                name: "RepliedAt",
                table: "ContactMessages");

            migrationBuilder.DropColumn(
                name: "RepliedByUserId",
                table: "ContactMessages");

            migrationBuilder.DropColumn(
                name: "ReplyAttemptId",
                table: "ContactMessages");

            migrationBuilder.DropColumn(
                name: "ReplyText",
                table: "ContactMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContactMessages_Status",
                table: "ContactMessages",
                sql: "([Status]='Unread' OR [Status]='Read')");
        }
    }
}
