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
