using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimWise.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddAiSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClaimId = table.Column<int>(type: "INTEGER", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    RetrievalQuery = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ClaimVersionAtGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratorName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EmbeddingModel = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AssessmentJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConflictsJson = table.Column<string>(type: "TEXT", nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RejectedJson = table.Column<string>(type: "TEXT", nullable: false),
                    AbstainReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReviewState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSummaries_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiSummaryPassages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SummaryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    ChunkId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DocumentVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ClauseRef = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Heading = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    SectionPath = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    RerankScore = table.Column<double>(type: "REAL", nullable: false),
                    KeywordScore = table.Column<double>(type: "REAL", nullable: true),
                    VectorScore = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSummaryPassages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSummaryPassages_AiSummaries_SummaryId",
                        column: x => x.SummaryId,
                        principalTable: "AiSummaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiSummaryStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SummaryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 1500, nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CitationsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSummaryStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSummaryStatements_AiSummaries_SummaryId",
                        column: x => x.SummaryId,
                        principalTable: "AiSummaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiSummaries_ClaimId_GeneratedAt",
                table: "AiSummaries",
                columns: new[] { "ClaimId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSummaryPassages_SummaryId",
                table: "AiSummaryPassages",
                column: "SummaryId");

            migrationBuilder.CreateIndex(
                name: "IX_AiSummaryStatements_SummaryId",
                table: "AiSummaryStatements",
                column: "SummaryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSummaryPassages");

            migrationBuilder.DropTable(
                name: "AiSummaryStatements");

            migrationBuilder.DropTable(
                name: "AiSummaries");
        }
    }
}
