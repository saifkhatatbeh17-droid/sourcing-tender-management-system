using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenderOfferExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The local database may already contain Yasmeen's migration from the other checkout.
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Tenders', 'OfferShortfallReason') IS NULL
    ALTER TABLE dbo.Tenders ADD OfferShortfallReason nvarchar(2000) NULL;
IF OBJECT_ID('dbo.TenderOfferExtensions', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.TenderOfferExtensions (
  TenderId int NOT NULL, Round int NOT NULL, PreviousDeadline datetime2 NOT NULL,
  OpenedAt datetime2 NOT NULL, Deadline datetime2 NOT NULL, PreviousCompliantOffers int NOT NULL,
  Outreach nvarchar(max) NOT NULL,
  CONSTRAINT PK_TenderOfferExtensions PRIMARY KEY (TenderId, Round),
  CONSTRAINT FK_TenderOfferExtensions_Tenders_TenderId FOREIGN KEY (TenderId) REFERENCES dbo.Tenders(TenderId)
 );
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenderOfferExtensions");

            migrationBuilder.DropColumn(
                name: "OfferShortfallReason",
                table: "Tenders");
        }
    }
}
