using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class MaterializePublishHistorySummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HistoryArtifactFileCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HistoryArtifactPackageSizeBytes",
                table: "publish_jobs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HistoryArtifactTotalSizeBytes",
                table: "publish_jobs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryLifecycleAmbiguousCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryLifecycleAppendTargetNotFoundCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryLifecycleCurrentSequenceCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryLifecycleDeleteTargetNotFoundCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryLifecycleMatchedCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryLifecycleReplaceTargetNotFoundCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryReadinessBlockingErrorCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HistoryReadinessIsReady",
                table: "publish_jobs",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryReadinessMissingMetadataFieldsJson",
                table: "publish_jobs",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryReadinessStatus",
                table: "publish_jobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryReadinessWarningCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HistoryReportAvailable",
                table: "publish_jobs",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryReportError",
                table: "publish_jobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryReportPath",
                table: "publish_jobs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HistoryReportReadable",
                table: "publish_jobs",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryValidationErrorCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryValidationProfile",
                table: "publish_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryValidationWarningCount",
                table: "publish_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryValidationWarningSummary",
                table: "publish_jobs",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_ApplicationId_HistoryReadinessStatus_CreatedUtc",
                table: "publish_jobs",
                columns: new[] { "ApplicationId", "HistoryReadinessStatus", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_publish_jobs_ApplicationId_HistoryReadinessStatus_CreatedUtc",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryArtifactFileCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryArtifactPackageSizeBytes",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryArtifactTotalSizeBytes",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryLifecycleAmbiguousCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryLifecycleAppendTargetNotFoundCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryLifecycleCurrentSequenceCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryLifecycleDeleteTargetNotFoundCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryLifecycleMatchedCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryLifecycleReplaceTargetNotFoundCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReadinessBlockingErrorCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReadinessIsReady",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReadinessMissingMetadataFieldsJson",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReadinessStatus",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReadinessWarningCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReportAvailable",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReportError",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReportPath",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryReportReadable",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryValidationErrorCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryValidationProfile",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryValidationWarningCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "HistoryValidationWarningSummary",
                table: "publish_jobs");
        }
    }
}
