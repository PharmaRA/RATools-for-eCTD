using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Ctd;

namespace RATools.Application.Applications;

public static class ImportedNodeDiagnostics
{
    public static IReadOnlyList<CtdBackfillDiagnostic> ForSequence(ApplicationImportBatch batch, string sequenceNumber) =>
        batch.Placements.Where(placement => placement.SequenceNumber == sequenceNumber)
            .Select(placement => placement.NodeInstanceId is not { } id
                ? new CtdBackfillDiagnostic(placement.Id, "NODE_SCHEMA_NOT_AVAILABLE",
                    $"Imported section '{placement.CtdSection}' is unbound. Original backbone context is retained for a compatible schema.")
                : batch.Graph.HasUnresolvedIdentity(id)
                    ? new CtdBackfillDiagnostic(placement.Id, "NODE_IDENTITY_UNRESOLVED",
                        "The imported node or an ancestor has missing or ambiguous identity metadata; explicit mapping is required.")
                    : batch.Nodes.Single(node => node.SequenceNumber == sequenceNumber && node.NodeInstanceId == id).MetadataStatus != NodeMetadataStatus.Complete
                        ? new CtdBackfillDiagnostic(placement.Id, "NODE_METADATA_INCOMPLETE", "Imported node metadata needs completion.") : null)
            .OfType<CtdBackfillDiagnostic>().ToArray();
}
