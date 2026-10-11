using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentPackageValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "package_validation_inputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    RootRelativePath = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SourceKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InputDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    LimitsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsOwnedUpload = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_validation_inputs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "package_validation_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Binding = table.Column<string>(type: "text", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    RequestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CancellationRequestedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FailureMessage = table.Column<string>(type: "text", nullable: true),
                    ReportSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_validation_runs", x => x.Id);
                    table.CheckConstraint("CK_package_validation_run_completion", "(\"Status\" IN ('Pending', 'Running')) = (\"CompletedUtc\" IS NULL)");
                    table.CheckConstraint("CK_package_validation_run_lease", "(\"Status\" = 'Running') = (\"LeaseToken\" IS NOT NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_package_validation_run_report", "(\"Status\" = 'Completed') = (\"ReportSha256\" IS NOT NULL)");
                    table.CheckConstraint("CK_package_validation_run_revision", "\"Revision\" >= 0 AND \"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_package_validation_run_status", "\"Status\" IN ('Pending', 'Running', 'Completed', 'Failed', 'Cancelled', 'InputChanged')");
                });

            migrationBuilder.CreateTable(
                name: "package_validation_reports",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_validation_reports", x => x.RunId);
                    table.ForeignKey(
                        name: "FK_package_validation_reports_package_validation_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "package_validation_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "package_validation_findings",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LogicalPath = table.Column<string>(type: "text", nullable: true),
                    Json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_validation_findings", x => new { x.RunId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_package_validation_findings_package_validation_reports_RunId",
                        column: x => x.RunId,
                        principalTable: "package_validation_reports",
                        principalColumn: "RunId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_package_validation_findings_RunId_RuleId_Severity_Status",
                table: "package_validation_findings",
                columns: new[] { "RunId", "RuleId", "Severity", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_package_validation_inputs_ApplicationId_CreatedUtc",
                table: "package_validation_inputs",
                columns: new[] { "ApplicationId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_package_validation_runs_ApplicationId_CreatedUtc",
                table: "package_validation_runs",
                columns: new[] { "ApplicationId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_package_validation_runs_IdempotencyKey",
                table: "package_validation_runs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_package_validation_runs_Status_CreatedUtc",
                table: "package_validation_runs",
                columns: new[] { "Status", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM package_validation_inputs) OR EXISTS (SELECT 1 FROM package_validation_runs) THEN
                    RAISE EXCEPTION 'Cannot remove independent validation storage while inputs or evidence exist.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "package_validation_findings");

            migrationBuilder.DropTable(
                name: "package_validation_inputs");

            migrationBuilder.DropTable(
                name: "package_validation_reports");

            migrationBuilder.DropTable(
                name: "package_validation_runs");
        }
    }
}
