using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// Migration class names and array arguments are fixed by EF schema history/scaffolding.
#pragma warning disable CA1711, CA1861

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDurablePublishJobQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "publish_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "publish_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatUtc",
                table: "publish_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresUtc",
                table: "publish_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "publish_jobs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "publish_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptUtc",
                table: "publish_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE publish_jobs SET \"IdempotencyKey\" = replace(\"Id\"::text, '-', ''), \"NextAttemptUtc\" = \"CreatedUtc\";");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "publish_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "NextAttemptUtc",
                table: "publish_jobs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_IdempotencyKey",
                table: "publish_jobs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_Status_LeaseExpiresUtc",
                table: "publish_jobs",
                columns: new[] { "Status", "LeaseExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_Status_NextAttemptUtc_CreatedUtc",
                table: "publish_jobs",
                columns: new[] { "Status", "NextAttemptUtc", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_publish_jobs_IdempotencyKey",
                table: "publish_jobs");

            migrationBuilder.DropIndex(
                name: "IX_publish_jobs_Status_LeaseExpiresUtc",
                table: "publish_jobs");

            migrationBuilder.DropIndex(
                name: "IX_publish_jobs_Status_NextAttemptUtc_CreatedUtc",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatUtc",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresUtc",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "NextAttemptUtc",
                table: "publish_jobs");
        }
    }
}

#pragma warning restore CA1711, CA1861
