using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalizedDraftRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finalized_draft_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizedSequenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BaseWorkspaceRevision = table.Column<long>(type: "bigint", nullable: false),
                    WorkspaceRevision = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OpenedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finalized_draft_revisions", x => x.Id);
                    table.CheckConstraint("CK_finalized_draft_revision", "\"BaseWorkspaceRevision\" >= 0 AND \"WorkspaceRevision\" > \"BaseWorkspaceRevision\"");
                    table.CheckConstraint("CK_finalized_draft_schema", "\"SchemaVersion\" = 1");
                    table.ForeignKey(
                        name: "FK_finalized_draft_revisions_finalized_sequences_FinalizedSequ~",
                        column: x => x.FinalizedSequenceId,
                        principalTable: "finalized_sequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finalized_draft_revisions_ApplicationId_SequenceNumber_Open~",
                table: "finalized_draft_revisions",
                columns: new[] { "ApplicationId", "SequenceNumber", "OpenedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_finalized_draft_revisions_IdempotencyKey",
                table: "finalized_draft_revisions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finalized_draft_revisions_Snapshot",
                table: "finalized_draft_revisions",
                column: "FinalizedSequenceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A draft records which delivered version the editable content continues
            // from. Dropping the table would silently re-open finalized sequences for
            // editing with no evidence of their continuation.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM finalized_draft_revisions) THEN
                    RAISE EXCEPTION 'Finalized draft revisions exist; the continuation boundary cannot be downgraded under committed evidence.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "finalized_draft_revisions");
        }
    }
}
