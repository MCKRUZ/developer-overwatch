using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ScopeProcessedAgentEventsToSessionSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ProcessedAgentEvents",
                table: "ProcessedAgentEvents");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedAgentEvents_SessionId",
                table: "ProcessedAgentEvents");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProcessedAgentEvents",
                table: "ProcessedAgentEvents",
                columns: new[] { "SessionId", "EventId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ProcessedAgentEvents",
                table: "ProcessedAgentEvents");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProcessedAgentEvents",
                table: "ProcessedAgentEvents",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedAgentEvents_SessionId",
                table: "ProcessedAgentEvents",
                column: "SessionId");
        }
    }
}
