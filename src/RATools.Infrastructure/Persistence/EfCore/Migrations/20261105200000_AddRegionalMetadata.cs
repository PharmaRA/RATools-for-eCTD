using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RATools.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionalMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "regional_metadata_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MetadataRevision = table.Column<long>(type: "bigint", nullable: false),
                    WorkspaceRevision = table.Column<long>(type: "bigint", nullable: true),
                    StandardsSnapshotId = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                    InheritedDefaultsId = table.Column<Guid>(type: "uuid", nullable: true),
                    InheritedDefaultsScope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LegacyDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Data = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DataSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regional_metadata_revisions", x => x.Id);
                    table.UniqueConstraint("AK_regional_metadata_scope_id", x => new { x.ApplicationId, x.ScopeKey, x.Id });
                    table.CheckConstraint("CK_regional_metadata_inheritance", "(\"InheritedDefaultsId\" IS NULL AND \"InheritedDefaultsScope\" IS NULL) OR (\"InheritedDefaultsId\" IS NOT NULL AND \"InheritedDefaultsScope\" IS NOT DISTINCT FROM 'application' AND \"SequenceNumber\" IS NOT NULL)");
                    table.CheckConstraint("CK_regional_metadata_revision", "\"MetadataRevision\" >= 1 AND \"MetadataRevision\" < 9007199254740991");
                    table.CheckConstraint("CK_regional_metadata_scope", "(\"SequenceNumber\" IS NULL AND \"ScopeKey\" = 'application' AND \"WorkspaceRevision\" IS NULL) OR (\"SequenceNumber\" IS NOT NULL AND \"WorkspaceRevision\" IS NOT NULL AND \"SequenceNumber\" ~ '^[0-9]{4}$' AND \"ScopeKey\" = \"SequenceNumber\" AND \"WorkspaceRevision\" >= 1 AND \"WorkspaceRevision\" < 9007199254740991)");
                    table.CheckConstraint("CK_regional_metadata_size", "octet_length(\"Data\") <= 8388608");
                    table.ForeignKey(
                        name: "FK_regional_metadata_revisions_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regional_metadata_revisions_regional_metadata_revisions_App~",
                        columns: x => new { x.ApplicationId, x.InheritedDefaultsScope, x.InheritedDefaultsId },
                        principalTable: "regional_metadata_revisions",
                        principalColumns: new[] { "ApplicationId", "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regional_metadata_revisions_sequences_ApplicationId_Sequenc~",
                        columns: x => new { x.ApplicationId, x.SequenceNumber },
                        principalTable: "sequences",
                        principalColumns: new[] { "ApplicationId", "SequenceNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regional_metadata_revisions_standards_profile_snapshots_Sta~",
                        column: x => x.StandardsSnapshotId,
                        principalTable: "standards_profile_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regional_metadata_document_contexts",
                columns: table => new
                {
                    MetadataId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlacementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NodeInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regional_metadata_document_contexts", x => new { x.MetadataId, x.PlacementId });
                    table.CheckConstraint("CK_regional_document_context_ordinal", "\"Ordinal\" >= 0");
                    table.ForeignKey(
                        name: "FK_regional_metadata_document_contexts_document_placements_App~",
                        columns: x => new { x.ApplicationId, x.SequenceNumber, x.PlacementId },
                        principalTable: "document_placements",
                        principalColumns: new[] { "ApplicationId", "SequenceNumber", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regional_metadata_document_contexts_regional_metadata_revis~",
                        columns: x => new { x.ApplicationId, x.SequenceNumber, x.MetadataId },
                        principalTable: "regional_metadata_revisions",
                        principalColumns: new[] { "ApplicationId", "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regional_metadata_document_contexts_sequence_nodes_Applicat~",
                        columns: x => new { x.ApplicationId, x.SequenceNumber, x.NodeInstanceId },
                        principalTable: "sequence_nodes",
                        principalColumns: new[] { "ApplicationId", "SequenceNumber", "NodeInstanceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regional_metadata_heads",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MetadataId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regional_metadata_heads", x => new { x.ApplicationId, x.ScopeKey });
                    table.ForeignKey(
                        name: "FK_regional_metadata_heads_regional_metadata_revisions_Applica~",
                        columns: x => new { x.ApplicationId, x.ScopeKey, x.MetadataId },
                        principalTable: "regional_metadata_revisions",
                        principalColumns: new[] { "ApplicationId", "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regional_metadata_node_contexts",
                columns: table => new
                {
                    MetadataId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regional_metadata_node_contexts", x => new { x.MetadataId, x.NodeInstanceId });
                    table.CheckConstraint("CK_regional_node_context_ordinal", "\"Ordinal\" >= 0");
                    table.ForeignKey(
                        name: "FK_regional_metadata_node_contexts_regional_metadata_revisions~",
                        columns: x => new { x.ApplicationId, x.SequenceNumber, x.MetadataId },
                        principalTable: "regional_metadata_revisions",
                        principalColumns: new[] { "ApplicationId", "ScopeKey", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regional_metadata_node_contexts_sequence_nodes_ApplicationI~",
                        columns: x => new { x.ApplicationId, x.SequenceNumber, x.NodeInstanceId },
                        principalTable: "sequence_nodes",
                        principalColumns: new[] { "ApplicationId", "SequenceNumber", "NodeInstanceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_document_contexts_ApplicationId_Sequence~1",
                table: "regional_metadata_document_contexts",
                columns: new[] { "ApplicationId", "SequenceNumber", "NodeInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_document_contexts_ApplicationId_Sequence~2",
                table: "regional_metadata_document_contexts",
                columns: new[] { "ApplicationId", "SequenceNumber", "PlacementId" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_document_contexts_ApplicationId_SequenceN~",
                table: "regional_metadata_document_contexts",
                columns: new[] { "ApplicationId", "SequenceNumber", "MetadataId" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_document_contexts_MetadataId_Ordinal",
                table: "regional_metadata_document_contexts",
                columns: new[] { "MetadataId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_heads_ApplicationId_ScopeKey_MetadataId",
                table: "regional_metadata_heads",
                columns: new[] { "ApplicationId", "ScopeKey", "MetadataId" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_node_contexts_ApplicationId_SequenceNumb~1",
                table: "regional_metadata_node_contexts",
                columns: new[] { "ApplicationId", "SequenceNumber", "NodeInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_node_contexts_ApplicationId_SequenceNumbe~",
                table: "regional_metadata_node_contexts",
                columns: new[] { "ApplicationId", "SequenceNumber", "MetadataId" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_node_contexts_MetadataId_Ordinal",
                table: "regional_metadata_node_contexts",
                columns: new[] { "MetadataId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_legacy_digest",
                table: "regional_metadata_revisions",
                columns: new[] { "ApplicationId", "ScopeKey", "LegacyDigest" },
                unique: true,
                filter: "\"LegacyDigest\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_revisions_ApplicationId_InheritedDefaults~",
                table: "regional_metadata_revisions",
                columns: new[] { "ApplicationId", "InheritedDefaultsScope", "InheritedDefaultsId" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_revisions_ApplicationId_SequenceNumber",
                table: "regional_metadata_revisions",
                columns: new[] { "ApplicationId", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_revisions_StandardsSnapshotId",
                table: "regional_metadata_revisions",
                column: "StandardsSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_regional_metadata_scope_revision",
                table: "regional_metadata_revisions",
                columns: new[] { "ApplicationId", "ScopeKey", "MetadataRevision" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION ratools_regional_metadata_immutable() RETURNS trigger LANGUAGE plpgsql AS $guard$
                BEGIN
                    RAISE EXCEPTION 'Regional metadata evidence is append-only' USING ERRCODE = '55000';
                END;
                $guard$;
                CREATE TRIGGER regional_metadata_revisions_immutable
                    BEFORE UPDATE OR DELETE ON regional_metadata_revisions
                    FOR EACH ROW EXECUTE FUNCTION ratools_regional_metadata_immutable();
                CREATE TRIGGER regional_metadata_revisions_no_truncate
                    BEFORE TRUNCATE ON regional_metadata_revisions
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_regional_metadata_immutable();
                CREATE TRIGGER regional_metadata_nodes_immutable
                    BEFORE UPDATE OR DELETE ON regional_metadata_node_contexts
                    FOR EACH ROW EXECUTE FUNCTION ratools_regional_metadata_immutable();
                CREATE TRIGGER regional_metadata_nodes_no_truncate
                    BEFORE TRUNCATE ON regional_metadata_node_contexts
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_regional_metadata_immutable();
                CREATE TRIGGER regional_metadata_documents_immutable
                    BEFORE UPDATE OR DELETE ON regional_metadata_document_contexts
                    FOR EACH ROW EXECUTE FUNCTION ratools_regional_metadata_immutable();
                CREATE TRIGGER regional_metadata_documents_no_truncate
                    BEFORE TRUNCATE ON regional_metadata_document_contexts
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_regional_metadata_immutable();
                CREATE TRIGGER regional_metadata_heads_no_delete
                    BEFORE DELETE ON regional_metadata_heads
                    FOR EACH ROW EXECUTE FUNCTION ratools_regional_metadata_immutable();
                CREATE TRIGGER regional_metadata_heads_no_truncate
                    BEFORE TRUNCATE ON regional_metadata_heads
                    FOR EACH STATEMENT EXECUTE FUNCTION ratools_regional_metadata_immutable();

                CREATE FUNCTION ratools_regional_metadata_head_advance() RETURNS trigger LANGUAGE plpgsql AS $guard$
                DECLARE previous_revision bigint; next_revision bigint;
                BEGIN
                    SELECT "MetadataRevision" INTO next_revision FROM regional_metadata_revisions
                    WHERE "Id" = NEW."MetadataId" AND "ApplicationId" = NEW."ApplicationId" AND "ScopeKey" = NEW."ScopeKey";
                    IF TG_OP = 'INSERT' THEN
                        IF next_revision IS DISTINCT FROM 1::bigint THEN
                            RAISE EXCEPTION 'Regional metadata heads must start at revision one' USING ERRCODE = '55000';
                        END IF;
                    ELSE
                        SELECT "MetadataRevision" INTO previous_revision FROM regional_metadata_revisions WHERE "Id" = OLD."MetadataId";
                        IF NEW."ApplicationId" IS DISTINCT FROM OLD."ApplicationId" OR NEW."ScopeKey" IS DISTINCT FROM OLD."ScopeKey"
                           OR next_revision IS DISTINCT FROM previous_revision + 1 THEN
                            RAISE EXCEPTION 'Regional metadata heads must advance one revision within their scope' USING ERRCODE = '55000';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $guard$;
                CREATE TRIGGER regional_metadata_heads_advance
                    BEFORE INSERT OR UPDATE ON regional_metadata_heads
                    FOR EACH ROW EXECUTE FUNCTION ratools_regional_metadata_head_advance();

                CREATE FUNCTION ratools_regional_document_context_scope() RETURNS trigger LANGUAGE plpgsql AS $guard$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM document_placements WHERE "Id" = NEW."PlacementId"
                        AND "ApplicationId" = NEW."ApplicationId" AND "SequenceNumber" = NEW."SequenceNumber"
                        AND "NodeInstanceId" IS NOT DISTINCT FROM NEW."NodeInstanceId") THEN
                        RAISE EXCEPTION 'Regional document context must match its placement scope and node' USING ERRCODE = '23503';
                    END IF;
                    RETURN NEW;
                END;
                $guard$;
                CREATE TRIGGER regional_metadata_documents_scope
                    BEFORE INSERT ON regional_metadata_document_contexts
                    FOR EACH ROW EXECUTE FUNCTION ratools_regional_document_context_scope();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Regional metadata evidence cannot be removed by a downgrade. Restore a separately retained database instead.");
        }
    }
}
