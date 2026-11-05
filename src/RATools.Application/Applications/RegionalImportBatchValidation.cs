using RATools.Application.Abstractions.Persistence;

namespace RATools.Application.Applications;

public static class RegionalImportBatchValidation
{
    public static void Verify(ApplicationImportBatch batch)
    {
        var revisions = batch.RegionalRevisions ?? []; var sources = batch.RegionalEvidence ?? [];
        if (revisions.Count > ImportEvidenceBudget.MaximumSequences || sources.Count > ImportEvidenceBudget.MaximumSources || sources.Sum(item => (long)item.ByteLength) > ImportEvidenceBudget.MaximumBytes)
            throw new InvalidDataException("Imported regional evidence exceeds its batch bound.");
        if (revisions.Select(item => item.Id).Distinct().Count() != revisions.Count || revisions.Select(item => item.SequenceNumber).Distinct().Count() != revisions.Count ||
            sources.Select(item => item.Id).Distinct().Count() != sources.Count || sources.Select(item => (item.SequenceNumber, item.RelativePath)).Distinct().Count() != sources.Count)
            throw new InvalidDataException("Duplicate imported revision or evidence scope.");
        foreach (var revision in revisions)
        {
            revision.Verify();
            var sequence = batch.Application.Sequences.SingleOrDefault(item => item.SequenceNumber == revision.SequenceNumber);
            if (sequence is null || revision.ApplicationId != batch.Application.Id || revision.MetadataRevision != 1 || revision.WorkspaceRevision != sequence.WorkspaceRevision + 1 || revision.IsReady)
                throw new InvalidDataException("Imported regional head does not match the new sequence and initial workspace revision.");
            var evidence = sources.Where(item => item.MetadataId == revision.Id).ToArray();
            if (evidence.Length == 0 || revision.Provenance.Count == 0 || revision.Provenance.Any(item => item.Origin != "Imported" || item.Status != "Unconfirmed" ||
                !evidence.Any(source => source.Id == item.SourceRevisionId && source.Sha256 == item.SourceDigest))) throw new InvalidDataException("Imported provenance must bind retained original evidence.");
            foreach (var context in revision.DocumentContexts)
                if (!batch.Placements.Any(item => item.Id == context.PlacementId && item.ApplicationId == revision.ApplicationId && item.SequenceNumber == revision.SequenceNumber && item.NodeInstanceId == context.NodeInstanceId))
                    throw new InvalidDataException("Imported context must bind the actual sequence placement and node.");
            foreach (var context in revision.NodeContexts)
                if (!batch.Nodes.Any(item => item.NodeInstanceId == context.NodeInstanceId && item.SequenceNumber == revision.SequenceNumber)) throw new InvalidDataException("Imported node context is outside its sequence.");
        }
        foreach (var source in sources)
        {
            source.Verify();
            if (source.ApplicationId != batch.Application.Id || !revisions.Any(item => item.Id == source.MetadataId && item.SequenceNumber == source.SequenceNumber && item.StandardsSnapshotId == source.StandardsSnapshotId))
                throw new InvalidDataException("Original evidence must match the exact regional revision and snapshot.");
        }
    }
}
