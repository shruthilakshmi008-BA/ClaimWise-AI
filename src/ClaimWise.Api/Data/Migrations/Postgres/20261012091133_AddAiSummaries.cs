using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClaimWise.Api.Data.Migrations.Postgres
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimId = table.Column<int>(type: "integer", nullable: false),
                    Question = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    RetrievalQuery = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClaimVersionAtGeneration = table.Column<int>(type: "integer", nullable: false),
                    GeneratorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EmbeddingModel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AssessmentJson = table.Column<string>(type: "text", nullable: false),
                    ConflictsJson = table.Column<string>(type: "text", nullable: false),
                    WarningsJson = table.Column<string>(type: "text", nullable: false),
                    RejectedJson = table.Column<string>(type: "text", nullable: false),
                    AbstainReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SummaryId = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    ChunkId = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    DocumentTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClauseRef = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Heading = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SectionPath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    RerankScore = table.Column<double>(type: "double precision", nullable: false),
                    KeywordScore = table.Column<double>(type: "double precision", nullable: true),
                    VectorScore = table.Column<double>(type: "double precision", nullable: true)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SummaryId = table.Column<int>(type: "integer", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    Origin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CitationsJson = table.Column<string>(type: "text", nullable: false)
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
