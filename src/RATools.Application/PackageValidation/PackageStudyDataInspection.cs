namespace RATools.Application.PackageValidation;

internal enum StudyDataReferenceStatus
{
    NotSelected, ReferenceUnavailable, InvalidContentReference, HistoryUnavailable, MissingContent,
    Read, Unavailable, InputChanged, LimitExceeded
}

internal sealed record StudyDataReference(StfDocumentReference Reference, StudyDataReferenceStatus Status,
    string? ContentPath, CapturedXptFacts? Xpt, PackageTrialSummaryFacts? TrialSummary);
internal sealed class StudyDataDocument(BoundStudyTaggingFile stf, IEnumerable<StudyDataReference> references)
{
    internal BoundStudyTaggingFile Stf { get; } = stf;
    internal IReadOnlyList<StudyDataReference> References { get; } = Array.AsReadOnly(references.ToArray());
}

// Candidate selection is not evidence of standardized datasets or FDA applicability.
internal sealed class PackageStudyDataInspection(string selectionDigest, bool complete, IEnumerable<StudyDataDocument> documents,
    bool scopeComplete = false)
{
    internal string SelectionDigest { get; } = selectionDigest;
    internal bool Complete { get; } = complete;
    // Target discovery and association traversal finished and the package was reverified.
    // A known candidate's unavailable content does not make unrelated rule scope unknown.
    internal bool ScopeComplete { get; } = scopeComplete;
    internal IReadOnlyList<StudyDataDocument> Documents { get; } = Array.AsReadOnly(documents.ToArray());
}
