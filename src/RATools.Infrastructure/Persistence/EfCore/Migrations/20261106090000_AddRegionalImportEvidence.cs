using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionalImportEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "regional_import_evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MetadataId = table.Column<Guid>(type: "uuid", nullable: false),
                    StandardsSnapshotId = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(230)", maxLength: 230, nullable: false),
                    OriginalBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regional_import_evidence", x => x.Id);
                    table.CheckConstraint("CK_regional_import_evidence_size", "octet_length(\"OriginalBytes\") BETWEEN 1 AND 8388608");
                    table.ForeignKey(
                        name: "FK_regional_import_evidence_regional_metadata_revisions_Applic~",
                        columns: x => new { x.ApplicationId, x.SequenceNumber, x.MetadataId },
                        principalTable: "regional_metadata_revisions",
                        principalColumns: new[] { "ApplicationId", "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regional_import_evidence_standards_profile_snapshots_Standa~",
                        column: x => x.StandardsSnapshotId,
                        principalTable: "standards_profile_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_regional_import_evidence_ApplicationId_SequenceNumber_Metad~",
                table: "regional_import_evidence",
                columns: new[] { "ApplicationId", "SequenceNumber", "MetadataId" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_import_evidence_ApplicationId_SequenceNumber_Relat~",
                table: "regional_import_evidence",
                columns: new[] { "ApplicationId", "SequenceNumber", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regional_import_evidence_StandardsSnapshotId",
                table: "regional_import_evidence",
                column: "StandardsSnapshotId");

            migrationBuilder.Sql("""
                CREATE TRIGGER regional_import_evidence_immutable
                    BEFORE UPDATE OR DELETE ON regional_import_evidence
                    FOR EACH ROW EXECUTE FUNCTION ratools_regional_metadata_immutable();
                CREATE TRIGGER regional_import_evidence_no_truncate
                    BEFORE TRUNCATE ON regional_import_evidence
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_regional_metadata_immutable();

                CREATE FUNCTION ratools_regional_import_evidence_scope() RETURNS trigger LANGUAGE plpgsql AS $guard$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM regional_metadata_revisions
                        WHERE "Id" = NEW."MetadataId" AND "ApplicationId" = NEW."ApplicationId"
                        AND "ScopeKey" = NEW."SequenceNumber" AND "SequenceNumber" = NEW."SequenceNumber"
                        AND "StandardsSnapshotId" = NEW."StandardsSnapshotId") THEN
                        RAISE EXCEPTION 'Import evidence must bind its exact regional scope and standards snapshot' USING ERRCODE = '23503';
                    END IF;
                    IF NEW."Sha256" IS DISTINCT FROM encode(sha256(NEW."OriginalBytes"), 'hex') THEN
                        RAISE EXCEPTION 'Import evidence original bytes must match their SHA-256' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $guard$;
                CREATE TRIGGER regional_import_evidence_scope
                    BEFORE INSERT ON regional_import_evidence
                    FOR EACH ROW EXECUTE FUNCTION ratools_regional_import_evidence_scope();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Original regional import evidence cannot be removed by a downgrade. Restore a separately retained database instead.");
        }
    }
}
