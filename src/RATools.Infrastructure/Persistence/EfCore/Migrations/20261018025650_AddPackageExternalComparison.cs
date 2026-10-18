using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageExternalComparison : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "package_validation_external_comparisons",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ToolVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReportSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DifferencesJson = table.Column<string>(type: "text", nullable: false),
                    ReportText = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_validation_external_comparisons", x => x.RunId);
                    table.ForeignKey(
                        name: "FK_package_validation_external_comparisons_package_validation_~",
                        column: x => x.RunId,
                        principalTable: "package_validation_reports",
                        principalColumn: "RunId",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM package_validation_external_comparisons) THEN
                    RAISE EXCEPTION 'External comparison evidence cannot be discarded by downgrade; export it and restore the pre-upgrade backup.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "package_validation_external_comparisons");
        }
    }
}
