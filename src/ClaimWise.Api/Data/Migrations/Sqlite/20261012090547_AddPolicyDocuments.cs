using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimWise.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddPolicyDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PolicyFamily = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChunkCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UploadedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyChunks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Heading = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ClauseRef = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    SectionPath = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyChunks_PolicyDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "PolicyDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyChunks_DocumentId_Ordinal",
                table: "PolicyChunks",
                columns: new[] { "DocumentId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocuments_Sha256",
                table: "PolicyDocuments",
                column: "Sha256",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyChunks");

            migrationBuilder.DropTable(
                name: "PolicyDocuments");
        }
    }
}
