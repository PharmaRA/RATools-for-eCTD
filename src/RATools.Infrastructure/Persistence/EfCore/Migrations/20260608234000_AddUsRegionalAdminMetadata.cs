using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddUsRegionalAdminMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 该迁移曾因缺少 [Migration] 特性而不被 EF 发现，部分已部署库通过手工 SQL
            // 补过这些列。此处用 IF NOT EXISTS 使补录迁移历史时的重放保持幂等：
            // 列已存在则无害跳过，仅写入 __EFMigrationsHistory 行。
            migrationBuilder.Sql("""ALTER TABLE sequences ADD COLUMN IF NOT EXISTS "FdaApplicantContactName" character varying(256);""");
            migrationBuilder.Sql("""ALTER TABLE sequences ADD COLUMN IF NOT EXISTS "FdaApplicantContactType" character varying(64);""");
            migrationBuilder.Sql("""ALTER TABLE sequences ADD COLUMN IF NOT EXISTS "FdaEmail" character varying(256);""");
            migrationBuilder.Sql("""ALTER TABLE sequences ADD COLUMN IF NOT EXISTS "FdaTelephone" character varying(64);""");
            migrationBuilder.Sql("""ALTER TABLE sequences ADD COLUMN IF NOT EXISTS "FdaTelephoneNumberType" character varying(64);""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FdaApplicantContactName",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaApplicantContactType",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaEmail",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaTelephone",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaTelephoneNumberType",
                table: "sequences");
        }
    }
}
