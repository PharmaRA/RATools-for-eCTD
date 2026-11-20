using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal enum StfReferenceStatus
{
    Resolved, InvalidReference, FutureReference, HistoryUnavailable, BackboneUnavailable,
    BackboneIncomplete, BackboneInvalid, LeafMissing, AmbiguousId, NotLeaf, LeafUnqualified
}

internal sealed record StfOwnerBinding(string BackboneLogicalPath, ParsedPackageLeaf Leaf, string? Heading, bool Qualified);
internal sealed record StfDocumentReference(ParsedXmlElement Element, string? Href, StfReferenceStatus Status,
    LeafAddress? Target, ParsedPackageLeaf? Leaf);

// These are exact-input associations, not an accumulative/cumulative or lifecycle acceptance decision.
internal sealed class BoundStudyTaggingFile(string sourceId, string inputDigest, bool isTarget, ParsedStudyTaggingFile document,
    bool complete, IEnumerable<StfOwnerBinding> owners, IEnumerable<StfDocumentReference> references, bool structureComplete = false)
{
    internal string SourceId { get; } = sourceId;
    internal string InputDigest { get; } = inputDigest;
    internal bool IsTarget { get; } = isTarget;
    internal ParsedStudyTaggingFile Document { get; } = document;
    internal bool Complete { get; } = complete;
    internal bool StructureComplete { get; } = structureComplete;
    internal IReadOnlyList<StfOwnerBinding> Owners { get; } = Array.AsReadOnly(owners.ToArray());
    internal IReadOnlyList<StfDocumentReference> References { get; } = Array.AsReadOnly(references.ToArray());
}

internal sealed class PackageStfBindings(bool complete, IEnumerable<BoundStudyTaggingFile> documents, bool targetScopeComplete = false)
{
    internal bool Complete { get; } = complete;
    // All association work finished and the target discovery/owner scope is qualified.
    // Unrelated historical reference failures do not change this flag; budget truncation does.
    internal bool TargetScopeComplete { get; } = targetScopeComplete;
    internal IReadOnlyList<BoundStudyTaggingFile> Documents { get; } = Array.AsReadOnly(documents.ToArray());
    internal static PackageStfBindings Unknown { get; } = new(false, []);
}
