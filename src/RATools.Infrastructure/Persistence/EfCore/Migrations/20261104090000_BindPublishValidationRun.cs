using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations;

public partial class BindPublishValidationRun : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>(name: "PackageValidationRunId", table: "publish_jobs", type: "uuid", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE publish_jobs IN ACCESS EXCLUSIVE MODE;
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM publish_jobs WHERE "PackageValidationRunId" IS NOT NULL) THEN
                RAISE EXCEPTION 'Publish validation references exist; downgrade would discard committed bindings.' USING ERRCODE = '55000';
              END IF;
            END $$;
            """);
        migrationBuilder.DropColumn(name: "PackageValidationRunId", table: "publish_jobs");
    }
}
