using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteTokenUsageAndActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "Sessions",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAt",
                table: "Sessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalCacheCreationInputTokens",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalCacheReadInputTokens",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalInputTokens",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalOutputTokens",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConversationTurnId",
                table: "GovernanceViolations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolExecutionId",
                table: "GovernanceViolations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheCreationInputTokens",
                table: "ConversationTurns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadInputTokens",
                table: "ConversationTurns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContainsRedactedSecret",
                table: "ConversationTurns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "ConversationTurns",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "ConversationTurns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "ConversationTurns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "ConversationTurns",
                type: "INTEGER",
                nullable: true);

            // Same backfill as the SQL Server migration AddSessionLastActivityAt: existing sessions
            // take their latest turn/tool timestamp, else StartedAt, so they don't all read as stale.
            migrationBuilder.Sql("""
                UPDATE Sessions
                SET LastActivityAt = COALESCE(
                    max(
                        COALESCE((SELECT MAX(Timestamp) FROM ConversationTurns WHERE SessionId = Sessions.Id),
                                 (SELECT MAX(Timestamp) FROM ToolExecutions WHERE SessionId = Sessions.Id)),
                        COALESCE((SELECT MAX(Timestamp) FROM ToolExecutions WHERE SessionId = Sessions.Id),
                                 (SELECT MAX(Timestamp) FROM ConversationTurns WHERE SessionId = Sessions.Id))),
                    StartedAt);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TotalCacheCreationInputTokens",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TotalCacheReadInputTokens",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TotalInputTokens",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TotalOutputTokens",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ConversationTurnId",
                table: "GovernanceViolations");

            migrationBuilder.DropColumn(
                name: "ToolExecutionId",
                table: "GovernanceViolations");

            migrationBuilder.DropColumn(
                name: "CacheCreationInputTokens",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "CacheReadInputTokens",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "ContainsRedactedSecret",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "ConversationTurns");
        }
    }
}
