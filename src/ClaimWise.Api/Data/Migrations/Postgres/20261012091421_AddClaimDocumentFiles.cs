using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimWise.Api.Data.Migrations.Postgres
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
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "ClaimDocuments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "ClaimDocuments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoredFileName",
                table: "ClaimDocuments",
                type: "character varying(80)",
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
