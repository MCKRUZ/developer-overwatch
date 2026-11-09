using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContainsRedactedSecretFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ContainsRedactedSecret",
                table: "ConversationTurns",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContainsRedactedSecret",
                table: "ConversationTurns");
        }
    }
}
