using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RATools.Infrastructure.Persistence.EfCore.Migrations;

[DbContext(typeof(RAToolsDbContext))]
[Migration("20261105190000_AddImmutableStandardsProfiles")]
public sealed partial class AddImmutableStandardsProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("standards_profile_snapshots", table => new
        {
            Id = table.Column<string>("character varying(71)", maxLength: 71, nullable: false),
            SchemaVersion = table.Column<int>("integer", nullable: false),
            TemplateKey = table.Column<string>("character varying(64)", maxLength: 64, nullable: false),
            RuleProfileAlias = table.Column<string>("character varying(128)", maxLength: 128, nullable: false),
            AcquiredUtc = table.Column<DateTime>("timestamp with time zone", nullable: false),
            Data = table.Column<string>("text", nullable: false),
            DataSha256 = table.Column<string>("character varying(64)", maxLength: 64, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_standards_profile_snapshots", row => row.Id);
            table.CheckConstraint("CK_standards_snapshot_id", "\"Id\" ~ '^sha256:[0-9a-f]{64}$'");
            table.CheckConstraint("CK_standards_snapshot_schema", "\"SchemaVersion\" = 1");
            table.CheckConstraint("CK_standards_snapshot_hash", "\"DataSha256\" ~ '^[0-9a-f]{64}$'");
            table.CheckConstraint("CK_standards_snapshot_size", "octet_length(\"Data\") <= 67108864");
        });
        migrationBuilder.CreateIndex("IX_standards_snapshot_template_profile_acquired", "standards_profile_snapshots",
            ["TemplateKey", "RuleProfileAlias", "AcquiredUtc"]);
        migrationBuilder.Sql("""
            CREATE FUNCTION ratools_guard_standards_snapshot() RETURNS trigger
            LANGUAGE plpgsql AS $guard$
            BEGIN
              RAISE EXCEPTION 'Standards snapshots are append-only; % is not permitted', TG_OP
                USING ERRCODE = '55000';
            END
            $guard$;
            CREATE TRIGGER standards_snapshots_append_only_row
              BEFORE UPDATE OR DELETE ON standards_profile_snapshots
              FOR EACH ROW EXECUTE FUNCTION ratools_guard_standards_snapshot();
            CREATE TRIGGER standards_snapshots_append_only_truncate
              BEFORE TRUNCATE ON standards_profile_snapshots
              FOR EACH STATEMENT EXECUTE FUNCTION ratools_guard_standards_snapshot();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Standards snapshots retain historical evidence. Migrate forward instead of removing it.");
}
