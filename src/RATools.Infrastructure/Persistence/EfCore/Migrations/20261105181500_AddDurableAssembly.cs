using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RATools.Infrastructure.Persistence.EfCore.Migrations;

[DbContext(typeof(RAToolsDbContext))]
[Migration("20261105181500_AddDurableAssembly")]
public sealed partial class AddDurableAssembly : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("assembly_plans", table => new
        {
            Id = table.Column<Guid>("uuid", nullable: false), ApplicationId = table.Column<Guid>("uuid", nullable: false),
            Data = table.Column<string>("text", nullable: false), Digest = table.Column<string>("character varying(64)", maxLength: 64, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_assembly_plans", row => row.Id);
            table.ForeignKey("FK_assembly_plans_applications_ApplicationId", row => row.ApplicationId, "applications", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable("assembly_executions", table => new
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
            table.PrimaryKey("PK_assembly_executions", row => row.Id);
            table.ForeignKey("FK_assembly_executions_assembly_plans_PlanId", row => row.PlanId, "assembly_plans", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_assembly_plans_ApplicationId", "assembly_plans", "ApplicationId");
        migrationBuilder.CreateIndex("IX_assembly_executions_PlanId", "assembly_executions", "PlanId", unique: true);
        migrationBuilder.CreateIndex("IX_assembly_executions_IdempotencyKey", "assembly_executions", "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex("IX_assembly_executions_Status_CreatedUtc", "assembly_executions", ["Status", "CreatedUtc"]);
        migrationBuilder.Sql("""
            CREATE TRIGGER assembly_plans_immutable BEFORE UPDATE OR DELETE ON assembly_plans
                FOR EACH ROW EXECUTE FUNCTION ratools_processing_evidence_immutable();
            CREATE TRIGGER assembly_plans_no_truncate BEFORE TRUNCATE ON assembly_plans
                FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
            CREATE FUNCTION ratools_assembly_execution_guard() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP <> 'UPDATE' THEN
                    RAISE EXCEPTION 'Assembly jobs are retained as evidence.' USING ERRCODE = '55000';
                END IF;
                IF (NEW."Id", NEW."PlanId", NEW."IdempotencyKey", NEW."CreatedUtc") IS DISTINCT FROM
                   (OLD."Id", OLD."PlanId", OLD."IdempotencyKey", OLD."CreatedUtc") THEN
                    RAISE EXCEPTION 'Assembly request identity is immutable.' USING ERRCODE = '55000';
                END IF;
                IF NEW."Data" IS DISTINCT FROM OLD."Data" THEN
                    IF (NEW."Data"::jsonb->'State'->'PlanDigest', NEW."Data"::jsonb->'State'->'RequestDigest') IS DISTINCT FROM
                       (OLD."Data"::jsonb->'State'->'PlanDigest', OLD."Data"::jsonb->'State'->'RequestDigest') THEN
                        RAISE EXCEPTION 'Assembly request binding is immutable.' USING ERRCODE = '55000';
                    END IF;
                END IF;
                IF OLD."Status" IN ('Completed', 'Failed', 'Cancelled', 'TimedOut') AND NEW IS DISTINCT FROM OLD THEN
                    RAISE EXCEPTION 'Terminal assembly evidence is immutable.' USING ERRCODE = '55000';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER assembly_execution_guard BEFORE UPDATE OR DELETE ON assembly_executions
                FOR EACH ROW EXECUTE FUNCTION ratools_assembly_execution_guard();
            CREATE TRIGGER assembly_executions_no_truncate BEFORE TRUNCATE ON assembly_executions
                FOR EACH STATEMENT EXECUTE FUNCTION ratools_processing_evidence_immutable();
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Assembly execution evidence is retained. Migrate forward instead of dropping evidence.");
}
