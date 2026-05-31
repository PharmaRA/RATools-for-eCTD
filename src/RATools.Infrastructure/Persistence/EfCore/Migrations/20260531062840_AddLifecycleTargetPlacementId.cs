using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddLifecycleTargetPlacementId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LifecycleTargetPlacementId",
                table: "document_placements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_placements_LifecycleTargetPlacementId",
                table: "document_placements",
                column: "LifecycleTargetPlacementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_document_placements_LifecycleTargetPlacementId",
                table: "document_placements");

            migrationBuilder.DropColumn(
                name: "LifecycleTargetPlacementId",
                table: "document_placements");
        }
    }
}
