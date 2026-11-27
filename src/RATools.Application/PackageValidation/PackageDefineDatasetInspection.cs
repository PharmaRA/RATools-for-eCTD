namespace RATools.Application.PackageValidation;

internal enum DefineDatasetInspectionStatus { Read, DefineUnavailable, InputChanged, Unavailable, LimitExceeded }
internal enum DefineDatasetContentStatus { Read, ReferenceUnavailable, UnsupportedPath, Missing, Unavailable, LimitExceeded }
internal sealed record DefineDatasetContent(DefineDatasetDeclaration Declaration, DefineDatasetContentStatus Status,
    string? LogicalPath, CapturedXptFacts? Xpt);

// TraversalComplete is not model conformance, STF/study identity or a regulatory result.
internal sealed class PackageDefineDatasetInspection(DefineDatasetInspectionStatus status, string selectionDigest,
    CapturedDefineFacts? define = null, bool traversalComplete = false, IEnumerable<DefineDatasetContent>? datasets = null)
{
    internal DefineDatasetInspectionStatus Status { get; } = status;
    internal string SelectionDigest { get; } = selectionDigest;
    internal CapturedDefineFacts? Define { get; } = define;
    internal bool TraversalComplete { get; } = traversalComplete;
    internal IReadOnlyList<DefineDatasetContent> Datasets { get; } = Array.AsReadOnly((datasets ?? []).ToArray());
}
