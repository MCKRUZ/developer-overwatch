using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddBudgetsAndPlansSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DeveloperEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    SubjectKey = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Period = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AmountUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeveloperEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    PlanName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MonthlyPriceUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    WindowLimitUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
