using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalizedSequenceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finalized_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceWorkspaceRevision = table.Column<long>(type: "bigint", nullable: false),
                    ManifestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StandardsProfileSnapshotId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IchEctdVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegionalModule1Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HistoryBaselineDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationInputManifestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValidationReportSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValidationMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsReadyForFinalization = table.Column<bool>(type: "boolean", nullable: false),
                    BlockingReasonsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByToolVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finalized_sequences", x => x.Id);
                    table.CheckConstraint("CK_finalized_sequences_ready", "(\"ValidationMode\" <> 'Formal') OR \"IsReadyForFinalization\"");
                    table.CheckConstraint("CK_finalized_sequences_revision", "\"SourceWorkspaceRevision\" >= 0 AND \"SourceWorkspaceRevision\" <= 9007199254740991");
                    table.CheckConstraint("CK_finalized_sequences_schema", "\"SchemaVersion\" = 1");
                    table.ForeignKey(
                        name: "FK_finalized_sequences_package_validation_runs_ValidationRunId",
                        column: x => x.ValidationRunId,
                        principalTable: "package_validation_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finalized_delivery_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizedSequenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RecordedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finalized_delivery_records", x => x.Id);
                    table.CheckConstraint("CK_delivery_records_schema", "\"SchemaVersion\" = 1");
                    table.ForeignKey(
                        name: "FK_finalized_delivery_records_finalized_sequences_FinalizedSeq~",
                        column: x => x.FinalizedSequenceId,
                        principalTable: "finalized_sequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finalized_delivery_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawText = table.Column<string>(type: "text", nullable: false),
                    RawTextSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RecordedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finalized_delivery_receipts", x => x.Id);
                    table.CheckConstraint("CK_delivery_receipts_kind", "\"Kind\" IN ('Transmission', 'TechnicalValidation', 'BusinessOutcome')");
                    table.CheckConstraint("CK_delivery_receipts_schema", "\"SchemaVersion\" = 1");
                    table.ForeignKey(
                        name: "FK_finalized_delivery_receipts_finalized_delivery_records_Deli~",
                        column: x => x.DeliveryRecordId,
                        principalTable: "finalized_delivery_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finalized_delivery_receipts_DeliveryRecordId_RecordedUtc",
                table: "finalized_delivery_receipts",
                columns: new[] { "DeliveryRecordId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_finalized_delivery_records_FinalizedSequenceId_RecordedUtc",
                table: "finalized_delivery_records",
                columns: new[] { "FinalizedSequenceId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_finalized_sequences_ApplicationId_SequenceNumber_CreatedUtc",
                table: "finalized_sequences",
                columns: new[] { "ApplicationId", "SequenceNumber", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_finalized_sequences_content",
                table: "finalized_sequences",
                columns: new[] { "ApplicationId", "SequenceNumber", "ManifestDigest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finalized_sequences_IdempotencyKey",
                table: "finalized_sequences",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finalized_sequences_ValidationRunId",
                table: "finalized_sequences",
                column: "ValidationRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM finalized_sequences) THEN
                    RAISE EXCEPTION 'Finalized snapshots cannot be discarded by downgrade; export them and restore the pre-upgrade backup.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "finalized_delivery_receipts");

            migrationBuilder.DropTable(
                name: "finalized_delivery_records");

            migrationBuilder.DropTable(
                name: "finalized_sequences");
        }
    }
}
