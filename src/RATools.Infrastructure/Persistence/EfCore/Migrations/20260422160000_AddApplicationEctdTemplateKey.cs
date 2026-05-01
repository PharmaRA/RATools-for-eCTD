using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    public partial class AddApplicationEctdTemplateKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EctdTemplateKey",
                table: "applications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "us-fda-ectd-3.2.2");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EctdTemplateKey",
                table: "applications");
        }
    }
}
