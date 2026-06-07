using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddSequencePublishingMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FdaApplicantName",
                table: "sequences",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaApplicationType",
                table: "sequences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaFormType",
                table: "sequences",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaSequenceDescription",
                table: "sequences",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaSubmissionSubtype",
                table: "sequences",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FdaSubmissionType",
                table: "sequences",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FdaApplicantName",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaApplicationType",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaFormType",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaSequenceDescription",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaSubmissionSubtype",
                table: "sequences");

            migrationBuilder.DropColumn(
                name: "FdaSubmissionType",
                table: "sequences");
        }
    }
}
