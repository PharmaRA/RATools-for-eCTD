using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.RegionalMetadata;
using RATools.Domain.RegionalMetadata;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreApplicationImportStore(RAToolsDbContext database,
    IApplicationRepository applications, IDocumentRepository documents, IDocumentPlacementRepository placements,
    IRegionalMetadataStore? regionalMetadata = null)
    : IApplicationImportStore
{
    public async Task SaveAsync(ApplicationImportBatch batch, CancellationToken cancellationToken = default)
    {
        try { await SaveWithinTransactionAsync(batch, cancellationToken); }
        catch (PersistenceRollbackException exception) { throw new ApplicationImportCleanupException(batch, exception); }
    }

    private async Task SaveWithinTransactionAsync(ApplicationImportBatch batch, CancellationToken cancellationToken)
    {
        // A new application has no existing writers. The outer transaction also covers
        // every node write; SaveSequenceAsync would start an invalid nested transaction.
        await new EfCorePersistenceTransaction(database).ExecuteAsync(async ct =>
        {
            RegionalImportBatchValidation.Verify(batch);
            await applications.AddAsync(batch.Application, ct);
            var nodes = new EfCoreCtdNodeRepository(database);
            foreach (var sequence in batch.Application.Sequences)
                await nodes.SaveWithinTransactionAsync(batch.Graph, sequence.SequenceNumber,
                    batch.Nodes.Where(node => node.SequenceNumber == sequence.SequenceNumber).ToArray(),
                    sequence.WorkspaceRevision, ct);
            foreach (var document in batch.Documents) await documents.AddAsync(document, ct);
            foreach (var placement in batch.Placements) await placements.AddAsync(placement, ct);
            foreach (var sequence in batch.Application.Sequences)
                database.NodeBackfillDiagnostics.AddRange(ImportedNodeDiagnostics.ForSequence(batch, sequence.SequenceNumber)
                    .Select(issue => new NodeBackfillDiagnosticRecord
                    {
                        ApplicationId = batch.Application.Id, SequenceNumber = sequence.SequenceNumber,
                        PlacementId = issue.PlacementId, Code = issue.Code, Message = issue.Message
                    }));
            database.ImportedBackbones.AddRange(batch.Backbones.Select(source => new ImportedBackboneRecord
            {
                ApplicationId = batch.Application.Id, SequenceNumber = source.SequenceNumber,
                RelativePath = source.RelativePath, Xml = source.Xml
            }));
            await database.SaveChangesAsync(ct);
            // App/sequence, nodes and placements must exist before the revision's
            // direct SQL insert and placement-scoped context FK checks execute.
            foreach (var revision in batch.RegionalRevisions ?? [])
                await (regionalMetadata ?? new EfCoreRegionalMetadataStore(database)).AppendAsync(revision, null, ct);
            database.Set<ImportedRegionalEvidenceRecord>().AddRange((batch.RegionalEvidence ?? []).Select(source => new ImportedRegionalEvidenceRecord
            {
                Id = source.Id, ApplicationId = source.ApplicationId, SequenceNumber = source.SequenceNumber, MetadataId = source.MetadataId,
                StandardsSnapshotId = source.StandardsSnapshotId, RelativePath = source.RelativePath, OriginalBytes = source.OriginalBytes, Sha256 = source.Sha256
            }));
            await database.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ImportedBackbone>> GetBackbonesAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default) => await database.ImportedBackbones.AsNoTracking()
        .Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
        .OrderBy(row => row.RelativePath).Select(row => new ImportedBackbone(row.SequenceNumber, row.RelativePath, row.Xml))
        .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<ImportedRegionalEvidence>> GetRegionalEvidenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        var rows = await database.Set<ImportedRegionalEvidenceRecord>().AsNoTracking().Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
            .OrderBy(row => row.RelativePath).ToArrayAsync(cancellationToken);
        var result = rows.Select(row => new ImportedRegionalEvidence(row.Id, row.ApplicationId, row.SequenceNumber, row.MetadataId, row.StandardsSnapshotId, row.RelativePath, row.OriginalBytes, row.Sha256)).ToArray();
        foreach (var source in result)
        {
            var revision = await (regionalMetadata ?? new EfCoreRegionalMetadataStore(database)).GetRevisionAsync(source.MetadataId, cancellationToken)
                ?? throw new InvalidDataException("Imported evidence has no regional archive.");
            if (revision.ApplicationId != applicationId || revision.SequenceNumber != sequenceNumber || revision.StandardsSnapshotId != source.StandardsSnapshotId)
                throw new InvalidDataException("Imported evidence scope or snapshot differs from the committed regional archive.");
        }
        return result;
    }
}
