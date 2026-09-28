using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenUsageAndCostTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "Sessions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TotalCacheCreationInputTokens",
                table: "Sessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalCacheReadInputTokens",
                table: "Sessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalInputTokens",
                table: "Sessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalOutputTokens",
                table: "Sessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CacheCreationInputTokens",
                table: "ConversationTurns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadInputTokens",
                table: "ConversationTurns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "ConversationTurns",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "ConversationTurns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "ConversationTurns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "ConversationTurns",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
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
                name: "CacheCreationInputTokens",
                table: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "CacheReadInputTokens",
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
