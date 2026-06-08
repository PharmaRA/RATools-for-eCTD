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
            migrationBuilder.AddColumn<string>(
                name: "FdaApplicantContactName",
                table: "sequences",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaApplicantContactType",
                table: "sequences",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaEmail",
                table: "sequences",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaTelephone",
                table: "sequences",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaTelephoneNumberType",
                table: "sequences",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
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
