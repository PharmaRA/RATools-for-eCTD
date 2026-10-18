using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class RefineFinalizationUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_finalized_sequences_content",
                table: "finalized_sequences");

            migrationBuilder.CreateIndex(
                name: "IX_finalized_sequences_content",
                table: "finalized_sequences",
                columns: new[] { "ApplicationId", "SequenceNumber", "ManifestDigest", "ValidationMode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The older index is stricter: downgrading would silently reject content
            // finalized in both modes, and the subsequent finalized-sequences guard would
            // abort only after this migration had already been rolled back. Refuse while
            // any committed snapshot exists so the refusal is a no-op, not a partial
            // downgrade under live evidence.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM finalized_sequences) THEN
                    RAISE EXCEPTION 'Finalized snapshots exist; the uniqueness refinement cannot be downgraded under committed evidence. Export and reconcile the snapshots first.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_finalized_sequences_content",
                table: "finalized_sequences");

            migrationBuilder.CreateIndex(
                name: "IX_finalized_sequences_content",
                table: "finalized_sequences",
                columns: new[] { "ApplicationId", "SequenceNumber", "ManifestDigest" },
                unique: true);
        }
    }
}
