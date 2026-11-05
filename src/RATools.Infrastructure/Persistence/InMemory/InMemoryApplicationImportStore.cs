using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.RegionalMetadata;
using RATools.Domain.RegionalMetadata;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryApplicationImportStore(IApplicationRepository applications,
    IDocumentRepository documents, IDocumentPlacementRepository placements, InMemoryCtdNodeRepository nodes,
    IRegionalMetadataStore? regionalMetadata = null)
    : IApplicationImportStore
{
    private readonly ConcurrentDictionary<(Guid, string), ImportedBackbone[]> _backbones = new();
    private readonly ConcurrentDictionary<(Guid, string), ImportedRegionalEvidence[]> _evidence = new();

    public Task SaveAsync(ApplicationImportBatch batch, CancellationToken cancellationToken = default) =>
        new InMemoryPersistenceTransaction().ExecuteAsync(async ct =>
        {
            RegionalImportBatchValidation.Verify(batch);
            if (await applications.GetAsync(batch.Application.Id, ct) is not null)
                throw new InvalidOperationException("An import requires a new application.");
            await applications.AddAsync(batch.Application, ct);
            foreach (var sequence in batch.Application.Sequences)
                await nodes.StageImportAsync(batch.Graph, sequence.SequenceNumber,
                    batch.Nodes.Where(node => node.SequenceNumber == sequence.SequenceNumber).ToArray(),
                    sequence.WorkspaceRevision, ImportedNodeDiagnostics.ForSequence(batch, sequence.SequenceNumber), ct);
            foreach (var document in batch.Documents) await documents.AddAsync(document, ct);
            foreach (var placement in batch.Placements) await placements.AddAsync(placement, ct);
            foreach (var revision in batch.RegionalRevisions ?? [])
                await (regionalMetadata ?? throw new InvalidOperationException("Regional imports require a regional metadata store.")).AppendAsync(revision, null, ct);
            foreach (var group in (batch.RegionalEvidence ?? []).GroupBy(source => source.SequenceNumber))
            {
                var saved = group.ToArray();
                InMemoryPersistenceTransaction.Current.Value!.Commits.Add(() => _evidence[(batch.Application.Id, group.Key)] = saved);
            }
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

    public async Task<IReadOnlyList<ImportedRegionalEvidence>> GetRegionalEvidenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = await applications.GetAsync(applicationId, cancellationToken);
        var values = application?.Sequences.Any(sequence => sequence.SequenceNumber == sequenceNumber) == true ? _evidence.GetValueOrDefault((applicationId, sequenceNumber)) ?? [] : [];
        foreach (var source in values) source.Verify();
        return values.ToArray();
    }
}
