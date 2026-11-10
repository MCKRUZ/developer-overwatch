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
            // Sessions may share an event id once it is scoped to the session; PK(EventId) keeps one row per id.
            migrationBuilder.Sql("""
                DELETE FROM "ProcessedAgentEvents"
                WHERE rowid NOT IN (SELECT MIN(rowid) FROM "ProcessedAgentEvents" GROUP BY "EventId");
                """);

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
