using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateContactMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.ContactMessages', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.ContactMessages (
 ContactMessageId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContactMessages PRIMARY KEY,
 UserId nvarchar(450) NULL, FullName nvarchar(120) NOT NULL, Email nvarchar(254) NOT NULL,
 Subject nvarchar(150) NOT NULL, Message nvarchar(2000) NOT NULL,
 SubmittedAt datetime2 NOT NULL, Status nvarchar(20) NOT NULL
 );
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactMessages",
                schema: "dbo");
        }
    }
}
