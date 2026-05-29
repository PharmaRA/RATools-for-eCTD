using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseConstraintsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_ApplicationId_CreatedUtc",
                table: "publish_jobs",
                columns: new[] { "ApplicationId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_ApplicationId_SequenceNumber",
                table: "publish_jobs",
                columns: new[] { "ApplicationId", "SequenceNumber" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_ApplicationId_SequenceNumber_CreatedUtc",
                table: "publish_jobs",
                columns: new[] { "ApplicationId", "SequenceNumber", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_ApplicationId_SequenceNumber_Status",
                table: "publish_jobs",
                columns: new[] { "ApplicationId", "SequenceNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_document_placements_ApplicationId_SequenceNumber",
                table: "document_placements",
                columns: new[] { "ApplicationId", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_document_placements_DocumentId",
                table: "document_placements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_applications_ApplicationNumber",
                table: "applications",
                column: "ApplicationNumber");

            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_applications_ApplicationNumber_lower\" ON applications (lower(\"ApplicationNumber\"));");

            migrationBuilder.AddForeignKey(
                name: "FK_document_placements_applications_ApplicationId",
                table: "document_placements",
                column: "ApplicationId",
                principalTable: "applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_document_placements_documents_DocumentId",
                table: "document_placements",
                column: "DocumentId",
                principalTable: "documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_placements_sequences_ApplicationId_SequenceNumber",
                table: "document_placements",
                columns: new[] { "ApplicationId", "SequenceNumber" },
                principalTable: "sequences",
                principalColumns: new[] { "ApplicationId", "SequenceNumber" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_document_placements_applications_ApplicationId",
                table: "document_placements");

            migrationBuilder.DropForeignKey(
                name: "FK_document_placements_documents_DocumentId",
                table: "document_placements");

            migrationBuilder.DropForeignKey(
                name: "FK_document_placements_sequences_ApplicationId_SequenceNumber",
                table: "document_placements");

            migrationBuilder.DropIndex(
                name: "IX_publish_jobs_ApplicationId_CreatedUtc",
                table: "publish_jobs");

            migrationBuilder.DropIndex(
                name: "IX_publish_jobs_ApplicationId_SequenceNumber",
                table: "publish_jobs");

            migrationBuilder.DropIndex(
                name: "IX_publish_jobs_ApplicationId_SequenceNumber_CreatedUtc",
                table: "publish_jobs");

            migrationBuilder.DropIndex(
                name: "IX_publish_jobs_ApplicationId_SequenceNumber_Status",
                table: "publish_jobs");

            migrationBuilder.DropIndex(
                name: "IX_document_placements_ApplicationId_SequenceNumber",
                table: "document_placements");

            migrationBuilder.DropIndex(
                name: "IX_document_placements_DocumentId",
                table: "document_placements");

            migrationBuilder.DropIndex(
                name: "IX_applications_ApplicationNumber",
                table: "applications");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_applications_ApplicationNumber_lower\";");
        }
    }
}
