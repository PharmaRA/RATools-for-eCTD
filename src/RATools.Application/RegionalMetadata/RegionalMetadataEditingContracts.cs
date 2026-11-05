using RATools.Domain.RegionalMetadata;

namespace RATools.Application.RegionalMetadata;

public sealed record RegionalMetadataUpdate(string StandardsSnapshotId, RegionalMetadataContent Content,
    IReadOnlyList<RegionalNodeContext>? NodeContexts = null, IReadOnlyList<RegionalDocumentContext>? DocumentContexts = null);
public sealed record RegionalSourceReferenceDto(string ResourceKey, string Sha256, string SourceUri, string Locator);
public sealed record RegionalFieldConditionDto(string FieldPath, IReadOnlyList<string> Values, string Description, string Effect = "guidance");
public sealed record RegionalFieldDescriptor(string Key, string Label, string Kind, bool Required, IReadOnlyList<string> Options,
    IReadOnlyList<RegionalFieldDescriptor> Fields, string? IdentityField = null, IReadOnlyList<RegionalFieldConditionDto>? Conditions = null,
    RegionalSourceReferenceDto? Source = null, string? BusinessEvidence = null);
public sealed record RegionalMetadataSchemaDto(int SchemaVersion, string RegionKey, string StandardsSnapshotId,
    IReadOnlyList<RegionalFieldDescriptor> SequenceFields, IReadOnlyList<RegionalFieldDescriptor> DefaultsFields,
    IReadOnlyList<RegionalFieldDescriptor> NodeContextFields, IReadOnlyList<RegionalFieldDescriptor> DocumentContextFields,
    IReadOnlyList<RegionalSourceReferenceDto> Sources, string QualificationStatus = "RegionalQualificationPending");
public sealed record RegionalDiagnosticDto(string Code, string FieldPath, string Message, RegionalSourceReferenceDto? Source = null);
public sealed record RegionalMetadataSummaryEntry(string FieldPath, string? Value, RegionalSourceReferenceDto? Source = null);
public sealed record RegionalMetadataPreflightDto(string StandardsSnapshotId, IReadOnlyList<RegionalDiagnosticDto> Diagnostics,
    IReadOnlyList<RegionalMetadataSummaryEntry> Summary);
public sealed record RegionalMetadataFieldDiff(string FieldPath, string? Before, string? After);
public sealed record RegionalMetadataPreviewDto(Guid SourceRevisionId, string SourceDigest, long ExpectedRevision,
    RegionalMetadataContent ProposedContent, IReadOnlyList<RegionalNodeContext> NodeContexts, IReadOnlyList<RegionalDocumentContext> DocumentContexts,
    IReadOnlyList<RegionalMetadataFieldDiff> FieldDiffs);
public sealed record RegionalMetadataRevisionDto(Guid Id, Guid ApplicationId, string? SequenceNumber, long MetadataRevision,
    long? WorkspaceRevision, string StandardsSnapshotId, RegionalMetadataContent? Content, RegionalApplicationDefaultsContent? Defaults,
    IReadOnlyList<RegionalNodeContext> NodeContexts, IReadOnlyList<RegionalDocumentContext> DocumentContexts, Guid? InheritedDefaultsId,
    string? InheritedDefaultsDigest, IReadOnlyList<RegionalMetadataProvenance> Provenance, IReadOnlyList<RegionalMetadataDiagnostic> Diagnostics,
    string Digest, DateTime CreatedUtc)
{
    public static RegionalMetadataRevisionDto? From(RegionalMetadataRevision? revision) => revision is null ? null : new(revision.Id,
        revision.ApplicationId, revision.SequenceNumber, revision.MetadataRevision, revision.WorkspaceRevision, revision.StandardsSnapshotId,
        revision.Content, revision.Defaults, revision.NodeContexts, revision.DocumentContexts, revision.InheritedDefaultsId,
        revision.InheritedDefaultsDigest, revision.Provenance, revision.Diagnostics, revision.Digest, revision.CreatedUtc);
}
