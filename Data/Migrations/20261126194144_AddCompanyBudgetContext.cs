using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyBudgetContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessImpact",
                table: "PurchaseRequests",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DelayCostPerDay",
                table: "PurchaseRequests",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedBudget",
                table: "PurchaseRequests",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyProfiles",
                schema: "dbo",
                columns: table => new
                {
                    CompanyProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Industry = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    FiscalYearStartMonth = table.Column<int>(type: "int", nullable: false),
                    AnnualRevenue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualProcurementBudget = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Employees = table.Column<int>(type: "int", nullable: false),
                    Sites = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Overview = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfiles", x => x.CompanyProfileId);
                    table.CheckConstraint("CK_CompanyProfiles_FiscalMonth", "[FiscalYearStartMonth] BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "DepartmentBudgets",
                schema: "dbo",
                columns: table => new
                {
                    DepartmentBudgetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SpentToDate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CommittedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentBudgets", x => x.DepartmentBudgetId);
                    table.ForeignKey(
                        name: "FK_DepartmentBudgets_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId");
                });

            migrationBuilder.CreateTable(
                name: "PastPurchases",
                schema: "dbo",
                columns: table => new
                {
                    PastPurchaseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PurchasedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PromisedDeliveryDays = table.Column<int>(type: "int", nullable: true),
                    ActualDeliveryDays = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PastPurchases", x => x.PastPurchaseId);
                    table.ForeignKey(
                        name: "FK_PastPurchases_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentBudgets_DepartmentId_FiscalYear",
                schema: "dbo",
                table: "DepartmentBudgets",
                columns: new[] { "DepartmentId", "FiscalYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PastPurchases_DepartmentId_PurchasedOn",
                schema: "dbo",
                table: "PastPurchases",
                columns: new[] { "DepartmentId", "PurchasedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProfiles",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DepartmentBudgets",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PastPurchases",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "BusinessImpact",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "DelayCostPerDay",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "EstimatedBudget",
                table: "PurchaseRequests");
        }
    }
}
