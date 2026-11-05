using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RATools.Infrastructure.Persistence.EfCore.Migrations;

[DbContext(typeof(RAToolsDbContext))]
[Migration("20261105120000_AddPdfRenameBatches")]
public sealed partial class AddPdfRenameBatches : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("pdf_rename_plans", table => new
        {
            Id = table.Column<Guid>("uuid", nullable: false), ApplicationId = table.Column<Guid>("uuid", nullable: false),
            Data = table.Column<string>("text", nullable: false), Digest = table.Column<string>("character varying(64)", maxLength: 64, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_pdf_rename_plans", row => row.Id);
            table.ForeignKey("FK_pdf_rename_plans_applications_ApplicationId", row => row.ApplicationId, "applications", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable("pdf_rename_jobs", table => new
        {
            Id = table.Column<Guid>("uuid", nullable: false), PlanId = table.Column<Guid>("uuid", nullable: false),
            IdempotencyKey = table.Column<string>("character varying(128)", maxLength: 128, nullable: false),
            Status = table.Column<string>("character varying(32)", maxLength: 32, nullable: false),
            CreatedUtc = table.Column<DateTime>("timestamp with time zone", nullable: false),
            LeaseExpiresUtc = table.Column<DateTime>("timestamp with time zone", nullable: true),
            QuarantinedUtc = table.Column<DateTime>("timestamp with time zone", nullable: true),
            Data = table.Column<string>("text", nullable: false), Digest = table.Column<string>("character varying(64)", maxLength: 64, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_pdf_rename_jobs", row => row.Id);
            table.ForeignKey("FK_pdf_rename_jobs_pdf_rename_plans_PlanId", row => row.PlanId, "pdf_rename_plans", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable("pdf_rename_adoptions", table => new
        {
            JobId = table.Column<Guid>("uuid", nullable: false), Data = table.Column<string>("text", nullable: false),
            Digest = table.Column<string>("character varying(64)", maxLength: 64, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_pdf_rename_adoptions", row => row.JobId);
            table.ForeignKey("FK_pdf_rename_adoptions_pdf_rename_jobs_JobId", row => row.JobId, "pdf_rename_jobs", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_pdf_rename_plans_ApplicationId", "pdf_rename_plans", "ApplicationId");
        migrationBuilder.CreateIndex("IX_pdf_rename_jobs_PlanId", "pdf_rename_jobs", "PlanId", unique: true);
        migrationBuilder.CreateIndex("IX_pdf_rename_jobs_IdempotencyKey", "pdf_rename_jobs", "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex("IX_pdf_rename_jobs_Status_CreatedUtc", "pdf_rename_jobs", ["Status", "CreatedUtc"]);
        migrationBuilder.Sql("""
            CREATE TRIGGER pdf_rename_plans_immutable BEFORE UPDATE OR DELETE ON pdf_rename_plans
                FOR EACH ROW EXECUTE FUNCTION ratools_processing_evidence_immutable();
            CREATE TRIGGER pdf_rename_plans_no_truncate BEFORE TRUNCATE ON pdf_rename_plans
                FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
            CREATE TRIGGER pdf_rename_adoptions_immutable BEFORE UPDATE OR DELETE ON pdf_rename_adoptions
                FOR EACH ROW EXECUTE FUNCTION ratools_processing_evidence_immutable();
            CREATE TRIGGER pdf_rename_adoptions_no_truncate BEFORE TRUNCATE ON pdf_rename_adoptions
                FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
            CREATE FUNCTION ratools_pdf_rename_job_guard() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP <> 'UPDATE' THEN
                    RAISE EXCEPTION 'PDF rename jobs are retained as evidence.' USING ERRCODE = '55000';
                END IF;
                IF (NEW."Id", NEW."PlanId", NEW."IdempotencyKey", NEW."CreatedUtc") IS DISTINCT FROM
                   (OLD."Id", OLD."PlanId", OLD."IdempotencyKey", OLD."CreatedUtc") THEN
                    RAISE EXCEPTION 'PDF rename request identity is immutable.' USING ERRCODE = '55000';
                END IF;
                IF NEW."Data" IS DISTINCT FROM OLD."Data" THEN
                    IF (NEW."Data"::jsonb->'State'->'PlanDigest', NEW."Data"::jsonb->'State'->'RequestDigest') IS DISTINCT FROM
                       (OLD."Data"::jsonb->'State'->'PlanDigest', OLD."Data"::jsonb->'State'->'RequestDigest') THEN
                        RAISE EXCEPTION 'PDF rename request identity is immutable.' USING ERRCODE = '55000';
                    END IF;
                END IF;
                IF OLD."Status" IN ('Completed', 'Failed', 'Cancelled', 'TimedOut') AND NEW IS DISTINCT FROM OLD THEN
                    RAISE EXCEPTION 'Terminal PDF rename evidence is immutable.' USING ERRCODE = '55000';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER pdf_rename_job_guard BEFORE UPDATE OR DELETE ON pdf_rename_jobs
                FOR EACH ROW EXECUTE FUNCTION ratools_pdf_rename_job_guard();
            CREATE TRIGGER pdf_rename_jobs_no_truncate BEFORE TRUNCATE ON pdf_rename_jobs
                FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("PDF rename evidence is retained. Migrate forward instead of dropping immutable batch evidence.");
}
