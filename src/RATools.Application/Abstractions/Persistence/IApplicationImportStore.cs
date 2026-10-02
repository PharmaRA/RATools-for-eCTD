using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;

namespace RATools.Application.Abstractions.Persistence;

// Archival input only: unknown regional attributes must never bypass the output schema.
public sealed record ImportedBackbone(string SequenceNumber, string RelativePath, string Xml);

public sealed record ApplicationImportBatch(SubmissionApplication Application, CtdNodeGraph Graph,
    IReadOnlyList<SequenceNode> Nodes, IReadOnlyCollection<SubmissionDocument> Documents,
    IReadOnlyList<DocumentPlacement> Placements, IReadOnlyList<ImportedBackbone> Backbones);

public interface IApplicationImportStore
{
    // All records belong to a new application and must commit or roll back together.
    Task SaveAsync(ApplicationImportBatch batch, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportedBackbone>> GetBackbonesAsync(Guid applicationId, string sequenceNumber,
        CancellationToken cancellationToken = default);
}
