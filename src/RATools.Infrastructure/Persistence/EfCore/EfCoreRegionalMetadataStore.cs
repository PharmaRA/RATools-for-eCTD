using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.RegionalMetadata;
using RATools.Domain.RegionalMetadata;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreRegionalMetadataStore(RAToolsDbContext context) : IRegionalMetadataStore, IRegionalEvidenceReferences
{
    public Task<bool> HasPlacementAsync(Guid placementId, CancellationToken cancellationToken = default) =>
        context.Set<RegionalDocumentContextRecord>().AnyAsync(row => row.PlacementId == placementId, cancellationToken);

    public async Task<bool> HasNodesAsync(Guid applicationId, string sequenceNumber, IReadOnlyCollection<Guid> nodeIds,
        CancellationToken cancellationToken = default) => nodeIds.Count > 0 &&
        (await context.Set<RegionalNodeContextRecord>().AnyAsync(row => row.ApplicationId == applicationId &&
            row.SequenceNumber == sequenceNumber && nodeIds.Contains(row.NodeInstanceId), cancellationToken).ConfigureAwait(false) ||
         await context.Set<RegionalDocumentContextRecord>().AnyAsync(row => row.ApplicationId == applicationId &&
            row.SequenceNumber == sequenceNumber && row.NodeInstanceId != null && nodeIds.Contains(row.NodeInstanceId.Value), cancellationToken).ConfigureAwait(false));

    internal static bool IsReferenceConflict(DbUpdateException error) => error.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.ForeignKeyViolation,
        ConstraintName: "FK_regional_metadata_document_contexts_document_placements_App~" or
            "FK_regional_metadata_document_contexts_sequence_nodes_Applicat~" or
            "FK_regional_metadata_node_contexts_sequence_nodes_ApplicationI~"
    };

    private const int MaxDataBytes = 8 * 1024 * 1024;
    public async Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default)
    {
        var scope = sequenceNumber ?? "application";
        var head = await context.Set<RegionalMetadataHeadRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ApplicationId == applicationId && row.ScopeKey == scope, cancellationToken).ConfigureAwait(false);
        if (head is null) return null;
        var revision = await GetRevisionAsync(head.MetadataId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The regional metadata head has no archived revision.");
        if (revision.ApplicationId != applicationId || revision.ScopeKey != scope) throw new InvalidDataException("The regional metadata head points outside its scope.");
        return revision;
    }
    public async Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Set<RegionalMetadataRecord>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        if (Encoding.UTF8.GetByteCount(row.Data) > MaxDataBytes || Hash(row.Data) != row.DataSha256)
            throw new InvalidDataException("The regional metadata archive failed byte integrity verification.");
        RegionalMetadataRevision revision;
        try { revision = JsonSerializer.Deserialize<RegionalMetadataRevision>(row.Data) ?? throw new InvalidDataException("Regional metadata is missing."); }
        catch (JsonException exception) { throw new InvalidDataException("The regional metadata archive is invalid.", exception); }
        revision.Verify();
        if (revision.Id != row.Id || revision.ApplicationId != row.ApplicationId || revision.ScopeKey != row.ScopeKey || revision.SequenceNumber != row.SequenceNumber ||
            revision.MetadataRevision != row.MetadataRevision || revision.WorkspaceRevision != row.WorkspaceRevision || revision.StandardsSnapshotId != row.StandardsSnapshotId ||
            revision.InheritedDefaultsId != row.InheritedDefaultsId || row.InheritedDefaultsScope != (revision.InheritedDefaultsId is null ? null : "application") ||
            revision.LegacyDigest != row.LegacyDigest || revision.Digest != row.Digest || Microseconds(revision.CreatedUtc) != row.CreatedUtc)
            throw new InvalidDataException("The regional metadata archive columns differ from the committed payload.");
        var nodes = await context.Set<RegionalNodeContextRecord>().AsNoTracking().Where(item => item.MetadataId == id).OrderBy(item => item.Ordinal).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var documents = await context.Set<RegionalDocumentContextRecord>().AsNoTracking().Where(item => item.MetadataId == id).OrderBy(item => item.Ordinal).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (nodes.Length != revision.NodeContexts.Count || documents.Length != revision.DocumentContexts.Count)
            throw new InvalidDataException("The regional metadata context inventory is incomplete.");
        for (var ordinal = 0; ordinal < nodes.Length; ordinal++)
        {
            var item = nodes[ordinal]; var expected = revision.NodeContexts[ordinal];
            if (item.Ordinal != ordinal || item.ApplicationId != revision.ApplicationId || item.SequenceNumber != revision.SequenceNumber ||
                item.NodeInstanceId != expected.NodeInstanceId || item.Data != JsonSerializer.Serialize(expected))
                throw new InvalidDataException("The regional node context differs from its committed scope or value.");
        }
        for (var ordinal = 0; ordinal < documents.Length; ordinal++)
        {
            var item = documents[ordinal]; var expected = revision.DocumentContexts[ordinal];
            if (item.Ordinal != ordinal || item.ApplicationId != revision.ApplicationId || item.SequenceNumber != revision.SequenceNumber ||
                item.PlacementId != expected.PlacementId || item.NodeInstanceId != expected.NodeInstanceId || item.Data != JsonSerializer.Serialize(expected))
                throw new InvalidDataException("The regional document context differs from its committed scope or value.");
        }
        return revision;
    }
    public async Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string legacyDigest, CancellationToken cancellationToken = default)
    {
        var id = await context.Set<RegionalMetadataRecord>().AsNoTracking()
            .Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber && row.LegacyDigest == legacyDigest)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return id is { } value ? await GetRevisionAsync(value, cancellationToken).ConfigureAwait(false) : null;
    }
    public async Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision); revision.Verify();
        var existing = await GetRevisionAsync(revision.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Digest != revision.Digest || existing.CreatedUtc != revision.CreatedUtc)
                throw new InvalidDataException("This metadata identity already carries different evidence.");
            return existing;
        }
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Appending regional metadata requires the workspace transaction.");
        var head = await GetHeadAsync(revision.ApplicationId, revision.SequenceNumber, cancellationToken).ConfigureAwait(false);
        if (head?.Id != expectedHeadId || revision.MetadataRevision != (head?.MetadataRevision ?? 0) + 1)
        {
            var replay = await GetRevisionAsync(revision.Id, cancellationToken).ConfigureAwait(false);
            if (replay is null) throw new RegionalMetadataConflictException();
            if (replay.Digest != revision.Digest || replay.CreatedUtc != revision.CreatedUtc)
                throw new InvalidDataException("This metadata identity already carries different evidence.");
            return replay;
        }
        if (revision.InheritedDefaultsId is { } inherited)
        {
            var defaults = await GetRevisionAsync(inherited, cancellationToken).ConfigureAwait(false);
            if (defaults is null || defaults.ApplicationId != revision.ApplicationId || defaults.SequenceNumber is not null || defaults.Digest != revision.InheritedDefaultsDigest)
                throw new RegionalMetadataScopeException("Inherited defaults do not belong to this application or exact revision.");
        }
        var data = JsonSerializer.Serialize(revision);
        if (Encoding.UTF8.GetByteCount(data) > MaxDataBytes) throw new InvalidDataException("The regional metadata archive exceeds its byte limit.");
        var record = new RegionalMetadataRecord { Id = revision.Id, ApplicationId = revision.ApplicationId, ScopeKey = revision.ScopeKey,
            SequenceNumber = revision.SequenceNumber, MetadataRevision = revision.MetadataRevision, WorkspaceRevision = revision.WorkspaceRevision,
            StandardsSnapshotId = revision.StandardsSnapshotId, InheritedDefaultsId = revision.InheritedDefaultsId,
            InheritedDefaultsScope = revision.InheritedDefaultsId is null ? null : "application", LegacyDigest = revision.LegacyDigest,
            Data = data, DataSha256 = Hash(data), Digest = revision.Digest, CreatedUtc = Microseconds(revision.CreatedUtc) };
        try
        {
            var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO regional_metadata_revisions ("Id", "ApplicationId", "ScopeKey", "SequenceNumber", "MetadataRevision", "WorkspaceRevision",
                    "StandardsSnapshotId", "InheritedDefaultsId", "InheritedDefaultsScope", "LegacyDigest", "Data", "Digest", "DataSha256", "CreatedUtc")
                VALUES ({record.Id}, {record.ApplicationId}, {record.ScopeKey}, {record.SequenceNumber}, {record.MetadataRevision}, {record.WorkspaceRevision},
                    {record.StandardsSnapshotId}, {record.InheritedDefaultsId}, {record.InheritedDefaultsScope}, {record.LegacyDigest}, {record.Data}, {record.Digest}, {record.DataSha256}, {record.CreatedUtc})
                ON CONFLICT ("Id") DO NOTHING
                """, cancellationToken).ConfigureAwait(false);
            if (inserted == 0)
            {
                var winner = await GetRevisionAsync(revision.Id, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("The competing metadata revision disappeared.");
                if (winner.Digest != revision.Digest || winner.CreatedUtc != revision.CreatedUtc) throw new InvalidDataException("This metadata identity already carries different evidence.");
                return winner;
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new RegionalMetadataConflictException(); }
        foreach (var (node, ordinal) in revision.NodeContexts.Select((node, ordinal) => (node, ordinal)))
            context.Add(new RegionalNodeContextRecord { MetadataId = revision.Id, ApplicationId = revision.ApplicationId, SequenceNumber = revision.SequenceNumber!,
                NodeInstanceId = node.NodeInstanceId, Ordinal = ordinal, Data = JsonSerializer.Serialize(node) });
        foreach (var (document, ordinal) in revision.DocumentContexts.Select((document, ordinal) => (document, ordinal)))
        {
            var placement = await context.DocumentPlacements.AsNoTracking().SingleOrDefaultAsync(item => item.Id == document.PlacementId, cancellationToken).ConfigureAwait(false);
            if (placement is null || placement.ApplicationId != revision.ApplicationId || placement.SequenceNumber != revision.SequenceNumber || placement.NodeInstanceId != document.NodeInstanceId)
                throw new RegionalMetadataScopeException("The regional context does not match its exact placement and node.");
            context.Add(new RegionalDocumentContextRecord { MetadataId = revision.Id, ApplicationId = revision.ApplicationId, SequenceNumber = revision.SequenceNumber!,
                PlacementId = document.PlacementId, NodeInstanceId = document.NodeInstanceId, Ordinal = ordinal, Data = JsonSerializer.Serialize(document) });
        }
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var changed = expectedHeadId is null
                ? await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO regional_metadata_heads ("ApplicationId", "ScopeKey", "MetadataId")
                    VALUES ({revision.ApplicationId}, {revision.ScopeKey}, {revision.Id}) ON CONFLICT ("ApplicationId", "ScopeKey") DO NOTHING
                    """, cancellationToken).ConfigureAwait(false)
                : await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE regional_metadata_heads SET "MetadataId" = {revision.Id}
                    WHERE "ApplicationId" = {revision.ApplicationId} AND "ScopeKey" = {revision.ScopeKey} AND "MetadataId" = {expectedHeadId.Value}
                    """, cancellationToken).ConfigureAwait(false);
            if (changed != 1) throw new RegionalMetadataConflictException();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new RegionalMetadataConflictException(); }
        return revision;
    }
    private static string Hash(string data) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    private static DateTime Microseconds(DateTime value) => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
}
