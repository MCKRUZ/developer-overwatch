using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionLastActivityAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAt",
                table: "Sessions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill from real activity so existing sessions don't all instantly read as stale
            // the moment this ships — use the latest known turn/tool timestamp per session,
            // falling back to StartedAt for sessions with no captured activity at all.
            migrationBuilder.Sql("""
                UPDATE s
                SET s.LastActivityAt = COALESCE(
                    (SELECT MAX(ts) FROM (
                        SELECT MAX(Timestamp) AS ts FROM ConversationTurns WHERE SessionId = s.Id
                        UNION ALL
                        SELECT MAX(Timestamp) AS ts FROM ToolExecutions WHERE SessionId = s.Id
                    ) activity),
                    s.StartedAt)
                FROM Sessions s
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "Sessions");
        }
    }
}
