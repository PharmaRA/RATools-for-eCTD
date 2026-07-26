using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_CreatedUtc",
                table: "audit_logs",
                column: "CreatedUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs",
                columns: new[] { "EntityType", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_logs_CreatedUtc",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs");
        }
    }
}
