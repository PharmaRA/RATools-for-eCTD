namespace RATools.Application.PackageValidation;

internal enum StfDefineInspectionStatus { Read, StfUnavailable, DefineUnavailable, InputChanged, Unavailable, LimitExceeded }
internal enum StfDatasetReferenceStatus { Referenced, MultipleLeaves, NotReferenced, Unknown }
internal sealed record DatasetStudyIdObservation(int RowIndex, bool Certain, bool Decoded, string? Value);
internal sealed class StfDefineDataset(DefineDatasetContent content, StfDatasetReferenceStatus referenceStatus,
    IEnumerable<StfDocumentReference> references, bool studyIdsComplete, IEnumerable<DatasetStudyIdObservation> studyIds)
{
    internal DefineDatasetContent Content { get; } = content;
    internal StfDatasetReferenceStatus ReferenceStatus { get; } = referenceStatus;
    internal IReadOnlyList<StfDocumentReference> References { get; } = Array.AsReadOnly(references.ToArray());
    internal bool StudyIdsComplete { get; } = studyIdsComplete;
    internal IReadOnlyList<DatasetStudyIdObservation> StudyIds { get; } = Array.AsReadOnly(studyIds.ToArray());
}

// Exact selected-STF/file associations only, not equivalence of ODM OIDs/STUDYID/STF IDs or FDA conformance.
internal sealed class PackageStfDefineInspection(StfDefineInspectionStatus status, string selectionDigest,
    BoundStudyTaggingFile? stf = null, string? studyId = null, PackageDefineDatasetInspection? define = null,
    IEnumerable<StfDefineDataset>? datasets = null)
{
    internal StfDefineInspectionStatus Status { get; } = status;
    internal string SelectionDigest { get; } = selectionDigest;
    internal BoundStudyTaggingFile? Stf { get; } = stf;
    internal string? StudyId { get; } = studyId;
    internal PackageDefineDatasetInspection? Define { get; } = define;
    internal IReadOnlyList<StfDefineDataset> Datasets { get; } = Array.AsReadOnly((datasets ?? []).ToArray());
}
