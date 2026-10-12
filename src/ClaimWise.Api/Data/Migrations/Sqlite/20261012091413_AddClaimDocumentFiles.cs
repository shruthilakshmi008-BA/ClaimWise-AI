using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimWise.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddClaimDocumentFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ClaimDocuments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "ClaimDocuments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "ClaimDocuments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoredFileName",
                table: "ClaimDocuments",
                type: "TEXT",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ClaimDocuments");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "ClaimDocuments");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "ClaimDocuments");

            migrationBuilder.DropColumn(
                name: "StoredFileName",
                table: "ClaimDocuments");
        }
    }
}
