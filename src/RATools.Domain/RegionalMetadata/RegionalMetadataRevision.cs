using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Domain.RegionalMetadata;

public sealed class RegionalMetadataRevision
{
    [JsonConstructor]
    public RegionalMetadataRevision(Guid id, Guid applicationId, string? sequenceNumber, long metadataRevision, long? workspaceRevision,
        string standardsSnapshotId, RegionalMetadataContent? content, RegionalApplicationDefaultsContent? defaults,
        IReadOnlyList<RegionalNodeContext> nodeContexts, IReadOnlyList<RegionalDocumentContext> documentContexts,
        Guid? inheritedDefaultsId, string? inheritedDefaultsDigest, IReadOnlyList<RegionalMetadataProvenance> provenance,
        IReadOnlyList<RegionalMetadataDiagnostic> diagnostics, string? legacyJson, string? legacyDigest, string digest, DateTime createdUtc)
    {
        RegionalMetadataJson.VerifyBudget(new { content, defaults, nodeContexts, documentContexts, provenance, diagnostics, legacyJson });
        RegionalMetadataRules.Id(id); RegionalMetadataRules.Id(applicationId);
        if (metadataRevision is < 1 or >= 9007199254740991L || createdUtc == default || createdUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Explicit revision and UTC creation time are required.");
        if (sequenceNumber is null)
        { if (content is not null || defaults is null || workspaceRevision is not null || nodeContexts.Count != 0 || documentContexts.Count != 0 || inheritedDefaultsId is not null) throw new ArgumentException("Application defaults cannot contain sequence content or contexts."); }
        else
        {
            PackageInputManifest.RequireSequenceNumber(sequenceNumber);
            if (content is null || defaults is not null || workspaceRevision is < 1 or >= 9007199254740991L || workspaceRevision is null) throw new ArgumentException("A sequence revision requires sequence content and its resulting workspace revision.");
        }
        if (!standardsSnapshotId.StartsWith("sha256:", StringComparison.Ordinal)) throw new ArgumentException("An exact standards snapshot is required.");
        ValidationBinding.RequireDigest(standardsSnapshotId[7..]);
        Id = id; ApplicationId = applicationId; SequenceNumber = sequenceNumber; MetadataRevision = metadataRevision; WorkspaceRevision = workspaceRevision;
        StandardsSnapshotId = standardsSnapshotId; Content = content is null ? null : RegionalMetadataRules.Freeze(content); Defaults = defaults is null ? null : RegionalMetadataRules.Freeze(defaults);
        NodeContexts = RegionalMetadataRules.Copy(nodeContexts, node => node.NodeInstanceId, node => { ContextText(node.Country, node.Language, node.ProductInformationType, node.FormType); return node with { }; });
        DocumentContexts = RegionalMetadataRules.Copy(documentContexts, document => document.PlacementId, document => { if (document.NodeInstanceId == Guid.Empty) throw new ArgumentException("A context node cannot be empty."); ContextText(document.Country, document.Language, document.ProductInformationType, document.FormType); return document with { }; });
        if ((inheritedDefaultsId is null) != (inheritedDefaultsDigest is null)) throw new ArgumentException("Inheritance must bind both source revision and digest.");
        if (inheritedDefaultsDigest is not null) ValidationBinding.RequireDigest(inheritedDefaultsDigest);
        InheritedDefaultsId = inheritedDefaultsId; InheritedDefaultsDigest = inheritedDefaultsDigest;
        if (provenance.Count > 20000 || diagnostics.Count > 20000) throw new ArgumentException("Regional evidence exceeds its item bounds.");
        foreach (var item in provenance)
        {
            if (item is null || item.Origin is not ("UserEntered" or "Imported" or "ApplicationDefaults" or "NodeDefaults" or "LegacyUnknown") || item.Status is not ("Unconfirmed" or "Confirmed")) throw new ArgumentException("Unknown metadata provenance.");
            RegionalMetadataRules.Text(item.FieldPath); if (item.SourceDigest is not null) ValidationBinding.RequireDigest(item.SourceDigest);
            if ((item.SourceRevisionId is null) != (item.SourceDigest is null)) throw new ArgumentException("Source provenance must bind both identity and digest.");
        }
        foreach (var item in diagnostics) { if (item is null) throw new ArgumentException("Null metadata diagnostic."); ContextText(item.Code, item.FieldPath, item.Message); }
        Provenance = Array.AsReadOnly(provenance.ToArray()); Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        if ((legacyJson is null) != (legacyDigest is null) || legacyJson?.Length > 2 * 1024 * 1024) throw new ArgumentException("Legacy evidence must be bounded and hash bound.");
        LegacyJson = legacyJson; LegacyDigest = legacyDigest; CreatedUtc = createdUtc; Digest = digest; Verify();
    }
    public Guid Id { get; }
    public Guid ApplicationId { get; }
    public string? SequenceNumber { get; }
    public long MetadataRevision { get; }
    public long? WorkspaceRevision { get; }
    public string StandardsSnapshotId { get; }
    public RegionalMetadataContent? Content { get; }
    public RegionalApplicationDefaultsContent? Defaults { get; }
    public IReadOnlyList<RegionalNodeContext> NodeContexts { get; }
    public IReadOnlyList<RegionalDocumentContext> DocumentContexts { get; }
    public Guid? InheritedDefaultsId { get; }
    public string? InheritedDefaultsDigest { get; }
    public IReadOnlyList<RegionalMetadataProvenance> Provenance { get; }
    public IReadOnlyList<RegionalMetadataDiagnostic> Diagnostics { get; }
    public string? LegacyJson { get; }
    public string? LegacyDigest { get; }
    public string Digest { get; }
    public DateTime CreatedUtc { get; }
    [JsonIgnore] public string ScopeKey => SequenceNumber ?? "application";
    [JsonIgnore] public string Scope => SequenceNumber is null ? "ApplicationDefaults" : "Sequence";
    [JsonIgnore] public int SchemaVersion => Content?.SchemaVersion ?? Defaults!.SchemaVersion;
    [JsonIgnore] public string RegionKey => Content?.RegionKey ?? Defaults!.RegionKey;
    public bool IsReady => Diagnostics.Count == 0 && Provenance.Count > 0 && Provenance.All(item => item.Status == "Confirmed");
    public static RegionalMetadataRevision Create(Guid id, Guid applicationId, string? sequenceNumber, long metadataRevision, long? workspaceRevision,
        string standardsSnapshotId, RegionalMetadataContent? content, RegionalApplicationDefaultsContent? defaults,
        IReadOnlyList<RegionalNodeContext> nodeContexts, IReadOnlyList<RegionalDocumentContext> documentContexts,
        Guid? inheritedDefaultsId, string? inheritedDefaultsDigest, IReadOnlyList<RegionalMetadataProvenance> provenance,
        IReadOnlyList<RegionalMetadataDiagnostic> diagnostics, string? legacyJson, string? legacyDigest, DateTime createdUtc)
    {
        RegionalMetadataJson.VerifyBudget(new { content, defaults, nodeContexts, documentContexts, provenance, diagnostics, legacyJson });
        var digest = Identity(id, applicationId, sequenceNumber, metadataRevision, workspaceRevision, standardsSnapshotId, content, defaults, nodeContexts, documentContexts,
            inheritedDefaultsId, inheritedDefaultsDigest, provenance, diagnostics, legacyJson, legacyDigest);
        return new(id, applicationId, sequenceNumber, metadataRevision, workspaceRevision, standardsSnapshotId, content, defaults, nodeContexts, documentContexts,
            inheritedDefaultsId, inheritedDefaultsDigest, provenance, diagnostics, legacyJson, legacyDigest, digest, createdUtc);
    }
    public void Verify()
    {
        if (LegacyJson is not null) { using var json = JsonDocument.Parse(LegacyJson); if (CanonicalJson.Digest(json.RootElement) != LegacyDigest) throw new InvalidDataException("Legacy metadata evidence changed."); }
        if (Digest != Identity(Id, ApplicationId, SequenceNumber, MetadataRevision, WorkspaceRevision, StandardsSnapshotId, Content, Defaults, NodeContexts, DocumentContexts,
            InheritedDefaultsId, InheritedDefaultsDigest, Provenance, Diagnostics, LegacyJson, LegacyDigest)) throw new InvalidDataException("Regional metadata revision identity failed verification.");
    }
    private static string Identity(Guid id, Guid applicationId, string? sequenceNumber, long metadataRevision, long? workspaceRevision, string standardsSnapshotId,
        RegionalMetadataContent? content, RegionalApplicationDefaultsContent? defaults, IReadOnlyList<RegionalNodeContext> nodeContexts, IReadOnlyList<RegionalDocumentContext> documentContexts,
        Guid? inheritedDefaultsId, string? inheritedDefaultsDigest, IReadOnlyList<RegionalMetadataProvenance> provenance, IReadOnlyList<RegionalMetadataDiagnostic> diagnostics,
        string? legacyJson, string? legacyDigest) => CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        { SchemaVersion = 1, id, applicationId, sequenceNumber, metadataRevision, workspaceRevision, standardsSnapshotId, content, defaults, nodeContexts, documentContexts,
            inheritedDefaultsId, inheritedDefaultsDigest, provenance, diagnostics, legacyJson, legacyDigest }));
    private static void ContextText(params string?[] values) { foreach (var value in values) RegionalMetadataRules.Text(value); }
}
