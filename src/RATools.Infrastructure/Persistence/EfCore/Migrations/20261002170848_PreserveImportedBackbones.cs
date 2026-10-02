using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class PreserveImportedBackbones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportedBackbonePath",
                table: "document_placements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportedHref",
                table: "document_placements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportedModifiedFile",
                table: "document_placements",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "imported_backbones",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(230)", maxLength: 230, nullable: false),
                    Xml = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imported_backbones", x => new { x.ApplicationId, x.SequenceNumber, x.RelativePath });
                    table.ForeignKey(
                        name: "FK_imported_backbones_sequences_ApplicationId_SequenceNumber",
                        columns: x => new { x.ApplicationId, x.SequenceNumber },
                        principalTable: "sequences",
                        principalColumns: new[] { "ApplicationId", "SequenceNumber" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM imported_backbones) OR
                       EXISTS (SELECT 1 FROM document_placements WHERE "ImportedBackbonePath" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Imported source data cannot be discarded by downgrade; export it and restore the pre-upgrade backup' USING ERRCODE = '55000';
                    END IF;
                END;
                $guard$;
                """);
            migrationBuilder.DropTable(
                name: "imported_backbones");

            migrationBuilder.DropColumn(
                name: "ImportedBackbonePath",
                table: "document_placements");

            migrationBuilder.DropColumn(
                name: "ImportedHref",
                table: "document_placements");

            migrationBuilder.DropColumn(
                name: "ImportedModifiedFile",
                table: "document_placements");
        }
    }
}
