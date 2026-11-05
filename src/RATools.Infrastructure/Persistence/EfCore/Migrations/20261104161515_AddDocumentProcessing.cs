using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Data = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_versions_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_versions_document_versions_ParentVersionId",
                        column: x => x.ParentVersionId,
                        principalTable: "document_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_versions_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_processing_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_processing_plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_processing_plans_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_processing_plans_document_versions_SourceVersionId",
                        column: x => x.SourceVersionId,
                        principalTable: "document_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_processing_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CancellationRequested = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ResultVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultPath = table.Column<string>(type: "text", nullable: true),
                    ResultSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResultMd5 = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_processing_jobs", x => x.Id);
                    table.CheckConstraint("CK_processing_attempts", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 3");
                    table.CheckConstraint("CK_processing_lease", "\"Status\" <> 'Running' OR (\"LeaseToken\" IS NOT NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_document_processing_jobs_document_processing_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "document_processing_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_processing_jobs_document_versions_ResultVersionId",
                        column: x => x.ResultVersionId,
                        principalTable: "document_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_processing_adoptions",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_processing_adoptions", x => x.JobId);
                    table.ForeignKey(
                        name: "FK_document_processing_adoptions_document_processing_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "document_processing_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_processing_adoptions_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_adoptions_DocumentId",
                table: "document_processing_adoptions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_jobs_IdempotencyKey",
                table: "document_processing_jobs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_jobs_PlanId",
                table: "document_processing_jobs",
                column: "PlanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_jobs_ResultVersionId",
                table: "document_processing_jobs",
                column: "ResultVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_jobs_Status_CreatedUtc",
                table: "document_processing_jobs",
                columns: new[] { "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_plans_ApplicationId",
                table: "document_processing_plans",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_plans_SourceVersionId",
                table: "document_processing_plans",
                column: "SourceVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_ApplicationId",
                table: "document_versions",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_ParentVersionId",
                table: "document_versions",
                column: "ParentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_SourceDocumentId",
                table: "document_versions",
                column: "SourceDocumentId");

            migrationBuilder.Sql("""
                CREATE FUNCTION ratools_processing_evidence_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Document processing evidence is append-only.' USING ERRCODE = '55000';
                END $$;
                CREATE TRIGGER document_versions_immutable BEFORE UPDATE OR DELETE ON document_versions
                    FOR EACH ROW EXECUTE FUNCTION ratools_processing_evidence_immutable();
                CREATE TRIGGER document_versions_no_truncate BEFORE TRUNCATE ON document_versions
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
                CREATE TRIGGER document_processing_plans_immutable BEFORE UPDATE OR DELETE ON document_processing_plans
                    FOR EACH ROW EXECUTE FUNCTION ratools_processing_evidence_immutable();
                CREATE TRIGGER document_processing_plans_no_truncate BEFORE TRUNCATE ON document_processing_plans
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
                CREATE TRIGGER document_processing_adoptions_immutable BEFORE UPDATE OR DELETE ON document_processing_adoptions
                    FOR EACH ROW EXECUTE FUNCTION ratools_processing_evidence_immutable();
                CREATE TRIGGER document_processing_adoptions_no_truncate BEFORE TRUNCATE ON document_processing_adoptions
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
                CREATE FUNCTION ratools_processing_job_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP <> 'UPDATE' THEN
                        RAISE EXCEPTION 'Processing jobs are retained as evidence.' USING ERRCODE = '55000';
                    END IF;
                    IF (NEW."Id", NEW."PlanId", NEW."PlanDigest", NEW."IdempotencyKey", NEW."RequestDigest", NEW."CreatedUtc")
                        IS DISTINCT FROM
                       (OLD."Id", OLD."PlanId", OLD."PlanDigest", OLD."IdempotencyKey", OLD."RequestDigest", OLD."CreatedUtc") THEN
                        RAISE EXCEPTION 'Processing request identity is immutable.' USING ERRCODE = '55000';
                    END IF;
                    IF OLD."Status" IN ('Completed', 'Failed', 'Cancelled', 'TimedOut') AND NEW IS DISTINCT FROM OLD THEN
                        RAISE EXCEPTION 'Terminal processing job is immutable.' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER document_processing_job_guard BEFORE UPDATE OR DELETE ON document_processing_jobs
                    FOR EACH ROW EXECUTE FUNCTION ratools_processing_job_guard();
                CREATE TRIGGER document_processing_jobs_no_truncate BEFORE TRUNCATE ON document_processing_jobs
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE document_versions, document_processing_plans, document_processing_jobs,
                    document_processing_adoptions IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM document_versions) OR EXISTS (SELECT 1 FROM document_processing_plans)
                        OR EXISTS (SELECT 1 FROM document_processing_jobs) OR EXISTS (SELECT 1 FROM document_processing_adoptions) THEN
                        RAISE EXCEPTION 'Document processing evidence exists; downgrade would discard it.' USING ERRCODE = '55000';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "document_processing_adoptions");

            migrationBuilder.DropTable(
                name: "document_processing_jobs");

            migrationBuilder.DropTable(
                name: "document_processing_plans");

            migrationBuilder.DropTable(
                name: "document_versions");
            migrationBuilder.Sql("DROP FUNCTION ratools_processing_job_guard(); DROP FUNCTION ratools_processing_evidence_immutable();");
        }
    }
}
