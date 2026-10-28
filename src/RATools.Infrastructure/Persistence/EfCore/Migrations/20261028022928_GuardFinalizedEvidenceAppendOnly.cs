using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class GuardFinalizedEvidenceAppendOnly : Migration
    {
        // Finalized snapshots and their delivery facts are evidence. The store only ever
        // inserts them, but a manual UPDATE/DELETE (or a mistaken maintenance script)
        // could rewrite history without any application code being involved. The trigger
        // makes that a database error instead of a silent edit.
        //
        // Row-level triggers cover the path a statement-level UPDATE trigger misses: an
        // INSERT ... ON CONFLICT DO UPDATE upsert is still an INSERT statement, yet it
        // rewrites the committed row. TRUNCATE only supports statement-level triggers,
        // so it is guarded separately.
        private const string Guard = """
            CREATE OR REPLACE FUNCTION ratools_guard_finalized_evidence() RETURNS trigger
            LANGUAGE plpgsql AS $guard$
            BEGIN
              RAISE EXCEPTION 'Finalized evidence is append-only; % is not permitted on %', TG_OP, TG_TABLE_NAME
                USING ERRCODE = '55000';
            END
            $guard$;
            """;

        private const string RemoveGuard = """
            DROP TRIGGER IF EXISTS finalized_sequences_append_only_row ON finalized_sequences;
            DROP TRIGGER IF EXISTS finalized_sequences_append_only_truncate ON finalized_sequences;
            DROP TRIGGER IF EXISTS finalized_delivery_records_append_only_row ON finalized_delivery_records;
            DROP TRIGGER IF EXISTS finalized_delivery_records_append_only_truncate ON finalized_delivery_records;
            DROP TRIGGER IF EXISTS finalized_delivery_receipts_append_only_row ON finalized_delivery_receipts;
            DROP TRIGGER IF EXISTS finalized_delivery_receipts_append_only_truncate ON finalized_delivery_receipts;
            DROP FUNCTION IF EXISTS ratools_guard_finalized_evidence();
            """;

        private const string CreateGuard = """
            DROP TRIGGER IF EXISTS finalized_sequences_append_only_row ON finalized_sequences;
            DROP TRIGGER IF EXISTS finalized_sequences_append_only_truncate ON finalized_sequences;
            DROP TRIGGER IF EXISTS finalized_delivery_records_append_only_row ON finalized_delivery_records;
            DROP TRIGGER IF EXISTS finalized_delivery_records_append_only_truncate ON finalized_delivery_records;
            DROP TRIGGER IF EXISTS finalized_delivery_receipts_append_only_row ON finalized_delivery_receipts;
            DROP TRIGGER IF EXISTS finalized_delivery_receipts_append_only_truncate ON finalized_delivery_receipts;
            CREATE TRIGGER finalized_sequences_append_only_row
              BEFORE UPDATE OR DELETE ON finalized_sequences
              FOR EACH ROW EXECUTE FUNCTION ratools_guard_finalized_evidence();
            CREATE TRIGGER finalized_sequences_append_only_truncate
              BEFORE TRUNCATE ON finalized_sequences
              FOR EACH STATEMENT EXECUTE FUNCTION ratools_guard_finalized_evidence();
            CREATE TRIGGER finalized_delivery_records_append_only_row
              BEFORE UPDATE OR DELETE ON finalized_delivery_records
              FOR EACH ROW EXECUTE FUNCTION ratools_guard_finalized_evidence();
            CREATE TRIGGER finalized_delivery_records_append_only_truncate
              BEFORE TRUNCATE ON finalized_delivery_records
              FOR EACH STATEMENT EXECUTE FUNCTION ratools_guard_finalized_evidence();
            CREATE TRIGGER finalized_delivery_receipts_append_only_row
              BEFORE UPDATE OR DELETE ON finalized_delivery_receipts
              FOR EACH ROW EXECUTE FUNCTION ratools_guard_finalized_evidence();
            CREATE TRIGGER finalized_delivery_receipts_append_only_truncate
              BEFORE TRUNCATE ON finalized_delivery_receipts
              FOR EACH STATEMENT EXECUTE FUNCTION ratools_guard_finalized_evidence();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Guard);
            migrationBuilder.Sql(CreateGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removing the guard destroys no evidence; the rows and their hashes remain
            // exactly as committed. Draft continuations were never guarded, so a rollback
            // leaves the schema in the state it had before this protection was added.
            migrationBuilder.Sql(RemoveGuard);
        }
    }
}
