using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryApplicationImportStore(IApplicationRepository applications,
    IDocumentRepository documents, IDocumentPlacementRepository placements, InMemoryCtdNodeRepository nodes)
    : IApplicationImportStore
{
    private readonly ConcurrentDictionary<(Guid, string), ImportedBackbone[]> _backbones = new();

    public Task SaveAsync(ApplicationImportBatch batch, CancellationToken cancellationToken = default) =>
        new InMemoryPersistenceTransaction().ExecuteAsync(async ct =>
        {
            if (await applications.GetAsync(batch.Application.Id, ct) is not null)
                throw new InvalidOperationException("An import requires a new application.");
            await applications.AddAsync(batch.Application, ct);
            foreach (var sequence in batch.Application.Sequences)
                await nodes.StageImportAsync(batch.Graph, sequence.SequenceNumber,
                    batch.Nodes.Where(node => node.SequenceNumber == sequence.SequenceNumber).ToArray(),
                    sequence.WorkspaceRevision, ImportedNodeDiagnostics.ForSequence(batch, sequence.SequenceNumber), ct);
            foreach (var document in batch.Documents) await documents.AddAsync(document, ct);
            foreach (var placement in batch.Placements) await placements.AddAsync(placement, ct);
            foreach (var sources in batch.Backbones.GroupBy(source => source.SequenceNumber))
            {
                var saved = sources.ToArray();
                InMemoryPersistenceTransaction.Current.Value!.Commits.Add(() =>
                    _backbones[(batch.Application.Id, sources.Key)] = saved);
            }
        }, cancellationToken);

    public async Task<IReadOnlyList<ImportedBackbone>> GetBackbonesAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = await applications.GetAsync(applicationId, cancellationToken);
        return application?.Sequences.Any(sequence => sequence.SequenceNumber == sequenceNumber) == true
            ? _backbones.GetValueOrDefault((applicationId, sequenceNumber))?.ToArray() ?? [] : [];
    }
}
