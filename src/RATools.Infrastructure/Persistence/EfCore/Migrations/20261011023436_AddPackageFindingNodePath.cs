using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageFindingNodePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NodePath",
                table: "package_validation_findings",
                type: "text",
                nullable: true);
            migrationBuilder.Sql("""
                UPDATE package_validation_findings
                SET "NodePath" = "Json"::jsonb #>> '{location,nodePath}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NodePath",
                table: "package_validation_findings");
        }
    }
}
