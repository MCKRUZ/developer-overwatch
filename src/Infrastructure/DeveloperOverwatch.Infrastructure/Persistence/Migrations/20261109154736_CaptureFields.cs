using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaptureFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivityState",
                table: "Sessions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivityStateAt",
                table: "Sessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Agent",
                table: "Sessions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "claude-code");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastToolErrorAt",
                table: "Sessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastToolErrorName",
                table: "Sessions",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "Sessions",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PeakContextTokens",
                table: "Sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectKey",
                table: "Sessions",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToolErrorCount",
                table: "Sessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ContextTokens",
                table: "ConversationTurns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitBranch",
                table: "ConversationTurns",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubagentName",
                table: "ConversationTurns",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivityState",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ActivityStateAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Agent",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LastToolErrorAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LastToolErrorName",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "MachineName",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PeakContextTokens",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ProjectKey",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ToolErrorCount",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ContextTokens",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "GitBranch",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "SubagentName",
                table: "ConversationTurns");
        }
    }
}
