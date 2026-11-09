using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperOverwatch.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PullRequestReviews",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PrNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Repo = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DeveloperEmail = table.Column<string>(type: "TEXT", nullable: false),
                    DeveloperName = table.Column<string>(type: "TEXT", nullable: true),
                    RepoPath = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommitSessionLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CommitSha = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    Repo = table.Column<string>(type: "TEXT", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: true),
                    LinkMethod = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitSessionLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitSessionLinks_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    TurnIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    TokenCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDecision = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationTurns_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceViolations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    ContextJson = table.Column<string>(type: "TEXT", nullable: true),
                    Resolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ResolutionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceViolations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernanceViolations_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToolExecutions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    TurnId = table.Column<string>(type: "TEXT", nullable: true),
                    ToolName = table.Column<string>(type: "TEXT", nullable: false),
                    InputParams = table.Column<string>(type: "TEXT", nullable: true),
                    OutputSummary = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolExecutions_ConversationTurns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "ConversationTurns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ToolExecutions_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArchitectAnnotations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    TurnId = table.Column<string>(type: "TEXT", nullable: true),
                    ToolExecutionId = table.Column<string>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchitectAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchitectAnnotations_ConversationTurns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "ConversationTurns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ArchitectAnnotations_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArchitectAnnotations_ToolExecutions_ToolExecutionId",
                        column: x => x.ToolExecutionId,
                        principalTable: "ToolExecutions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FileChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    ToolExecutionId = table.Column<string>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeHash = table.Column<string>(type: "TEXT", nullable: true),
                    AfterHash = table.Column<string>(type: "TEXT", nullable: true),
                    DiffText = table.Column<string>(type: "TEXT", nullable: true),
                    ChangeType = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileChanges_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileChanges_ToolExecutions_ToolExecutionId",
                        column: x => x.ToolExecutionId,
                        principalTable: "ToolExecutions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectAnnotations_SessionId",
                table: "ArchitectAnnotations",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectAnnotations_ToolExecutionId",
                table: "ArchitectAnnotations",
                column: "ToolExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectAnnotations_TurnId",
                table: "ArchitectAnnotations",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_CommitSessionLinks_CommitSha",
                table: "CommitSessionLinks",
                column: "CommitSha");

            migrationBuilder.CreateIndex(
                name: "IX_CommitSessionLinks_SessionId",
                table: "CommitSessionLinks",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_SessionId",
                table: "ConversationTurns",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_FileChanges_SessionId",
                table: "FileChanges",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_FileChanges_ToolExecutionId",
                table: "FileChanges",
                column: "ToolExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceViolations_CreatedAt",
                table: "GovernanceViolations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceViolations_SessionId",
                table: "GovernanceViolations",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceViolations_Severity",
                table: "GovernanceViolations",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviews_Repo_PrNumber",
                table: "PullRequestReviews",
                columns: new[] { "Repo", "PrNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_DeveloperEmail",
                table: "Sessions",
                column: "DeveloperEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Status",
                table: "Sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ToolExecutions_SessionId",
                table: "ToolExecutions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolExecutions_TurnId",
                table: "ToolExecutions",
                column: "TurnId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchitectAnnotations");

            migrationBuilder.DropTable(
                name: "CommitSessionLinks");

            migrationBuilder.DropTable(
                name: "FileChanges");

            migrationBuilder.DropTable(
                name: "GovernanceViolations");

            migrationBuilder.DropTable(
                name: "PullRequestReviews");

            migrationBuilder.DropTable(
                name: "ToolExecutions");

            migrationBuilder.DropTable(
                name: "ConversationTurns");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
