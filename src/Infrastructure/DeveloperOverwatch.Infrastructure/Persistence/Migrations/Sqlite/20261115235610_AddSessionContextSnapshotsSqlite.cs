using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddSessionContextSnapshotsSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PeakContextModel",
                table: "Sessions",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TurnKind",
                table: "ConversationTurns",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionContextSnapshots",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    TakenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClaudeCodeVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MeasuredContextTokens = table.Column<long>(type: "INTEGER", nullable: false)
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Server = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false)
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PreTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    PostTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false)
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Group = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IsMcp = table.Column<bool>(type: "INTEGER", nullable: false),
                    ToolCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TokensPerRequest = table.Column<long>(type: "INTEGER", nullable: false),
                    Used = table.Column<bool>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false)
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
