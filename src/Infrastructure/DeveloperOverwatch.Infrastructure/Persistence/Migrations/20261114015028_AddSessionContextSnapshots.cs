using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionContextSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PeakContextModel",
                table: "Sessions",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TurnKind",
                table: "ConversationTurns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionContextSnapshots",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TakenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClaudeCodeVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MeasuredContextTokens = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionContextSnapshots", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_SessionContextSnapshots_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionContextCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Server = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Tokens = table.Column<long>(type: "bigint", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionContextCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionContextCategories_SessionContextSnapshots_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SessionContextSnapshots",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionContextCompactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreTokens = table.Column<long>(type: "bigint", nullable: false),
                    PostTokens = table.Column<long>(type: "bigint", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionContextCompactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionContextCompactions_SessionContextSnapshots_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SessionContextSnapshots",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionContextTools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Group = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsMcp = table.Column<bool>(type: "bit", nullable: false),
                    ToolCount = table.Column<int>(type: "int", nullable: false),
                    TokensPerRequest = table.Column<long>(type: "bigint", nullable: false),
                    Used = table.Column<bool>(type: "bit", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionContextTools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionContextTools_SessionContextSnapshots_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SessionContextSnapshots",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionContextCategories_SessionId",
                table: "SessionContextCategories",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionContextCompactions_SessionId",
                table: "SessionContextCompactions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionContextTools_SessionId",
                table: "SessionContextTools",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionContextCategories");

            migrationBuilder.DropTable(
                name: "SessionContextCompactions");

            migrationBuilder.DropTable(
                name: "SessionContextTools");

            migrationBuilder.DropTable(
                name: "SessionContextSnapshots");

            migrationBuilder.DropColumn(
                name: "PeakContextModel",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TurnKind",
                table: "ConversationTurns");
        }
    }
}
