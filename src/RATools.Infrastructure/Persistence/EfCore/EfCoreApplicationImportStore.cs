using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreApplicationImportStore(RAToolsDbContext database,
    IApplicationRepository applications, IDocumentRepository documents, IDocumentPlacementRepository placements)
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
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ImportedBackbone>> GetBackbonesAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default) => await database.ImportedBackbones.AsNoTracking()
        .Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
        .OrderBy(row => row.RelativePath).Select(row => new ImportedBackbone(row.SequenceNumber, row.RelativePath, row.Xml))
        .ToArrayAsync(cancellationToken);
}
