using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageValidationCoverageSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverageSummary",
                table: "package_validation_runs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM package_validation_runs) THEN
                    RAISE EXCEPTION 'Cannot remove archived validation coverage while runs exist.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "CoverageSummary",
                table: "package_validation_runs");
        }
    }
}
