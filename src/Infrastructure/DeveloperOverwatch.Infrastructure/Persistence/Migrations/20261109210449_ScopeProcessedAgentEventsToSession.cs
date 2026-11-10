using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeProcessedAgentEventsToSession : Migration
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
                columns: new[] { "SessionId", "EventId" })
                .Annotation("SqlServer:Clustered", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sessions may share an event id once it is scoped to the session; PK(EventId) keeps one row per id.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT ROW_NUMBER() OVER (PARTITION BY [EventId] ORDER BY [ProcessedAt], [SessionId]) AS [Rank]
                    FROM [ProcessedAgentEvents])
                DELETE FROM ranked WHERE [Rank] > 1;
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
