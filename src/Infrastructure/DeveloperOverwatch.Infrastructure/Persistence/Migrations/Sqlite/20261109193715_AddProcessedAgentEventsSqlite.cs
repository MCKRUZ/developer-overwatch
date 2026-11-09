using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddProcessedAgentEventsSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedAgentEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedAgentEvents", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedAgentEvents_SessionId",
                table: "ProcessedAgentEvents",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedAgentEvents");
        }
    }
}
