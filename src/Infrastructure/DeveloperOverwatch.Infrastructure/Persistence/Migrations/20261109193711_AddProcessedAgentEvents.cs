using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedAgentEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedAgentEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
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
