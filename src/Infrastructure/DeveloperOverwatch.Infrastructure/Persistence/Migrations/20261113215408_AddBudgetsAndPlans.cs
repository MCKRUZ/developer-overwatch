using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetsAndPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DeveloperEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    SubjectKey = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Period = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AmountUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeveloperEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PlanName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonthlyPriceUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    WindowLimitUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperPlans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_SubjectKey_Period",
                table: "Budgets",
                columns: new[] { "SubjectKey", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperPlans_DeveloperEmail",
                table: "DeveloperPlans",
                column: "DeveloperEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "DeveloperPlans");
        }
    }
}
