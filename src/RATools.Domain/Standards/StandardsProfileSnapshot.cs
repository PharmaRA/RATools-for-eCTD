using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Domain.Standards;

public sealed record StandardsBusinessContext(string ApplicationType, string ProcedureType, string Country,
    string Language, string SectionScope, string Operation);
public enum StandardsSnapshotStatus { Active, AcquiredNotActive, Retired }
public sealed record StandardsSnapshotLifecycle(DateOnly EffectiveFrom, DateOnly? RetiredFrom, StandardsSnapshotStatus Status, string RetirementPolicy);
public sealed record StandardsSnapshotProvenance(string SourceUrl, string Version, DateOnly? AcquiredOn, DateOnly? VerifiedOn, string VerificationStatus, string? Sha256);
public sealed record StandardsSnapshotEvidence(string Category, string Status, string Reason, string? EvidenceSha256 = null);
public sealed record StandardsBusinessScope(string ApplicationType, string ProcedureType, string Country, string Language,
    string SectionScope, string Operation, bool CreateDraft, bool TrialPublish, bool Finalize, string EvidenceStatus)
{
    public bool Matches(StandardsBusinessContext context) => Match(ApplicationType, context.ApplicationType) && Match(ProcedureType, context.ProcedureType) &&
        Match(Country, context.Country) && Match(Language, context.Language) && Match(SectionScope, context.SectionScope) && Match(Operation, context.Operation);
    private static bool Match(string scope, string value) => !string.IsNullOrWhiteSpace(value) && (scope == "*" || scope == value);
}
public sealed record StandardsSnapshotDescriptor(string TemplateKey, string DisplayName, string RegulatoryAgency, string Region,
    string IchEctdVersion, string? RegionalModule1Version, string GuideVersion, string CriteriaVersion, string RuleProfileAlias,
    StandardsSnapshotLifecycle Lifecycle, IReadOnlyList<StandardsSnapshotProvenance> Provenance,
    IReadOnlyList<StandardsBusinessScope> Scopes, IReadOnlyList<StandardsSnapshotEvidence> Evidence);

public sealed class StandardsSnapshotResource
{
    [JsonConstructor]
    public StandardsSnapshotResource(string key, string category, string relativePath, string mediaType, long length, string sha256, string contentBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key); ArgumentException.ThrowIfNullOrWhiteSpace(category); ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (key.Length > 128 || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            throw new ArgumentException("Resource keys must be bounded simple identifiers.", nameof(key));
        if (relativePath.Length > 230 || relativePath.Contains('\\') || relativePath.Contains(':') || relativePath.StartsWith('/') ||
            relativePath.Split('/').Any(part => part is "" or "." or "..")) throw new ArgumentException("Resource paths must be safe relative paths.", nameof(relativePath));
        if (relativePath.Split('/').Any(part => part != PortablePathSegment.NormalizeAndValidate(part, nameof(relativePath))))
            throw new ArgumentException("Resource paths require canonical portable segments.", nameof(relativePath));
        if (length is < 0 or > 8 * 1024 * 1024 || contentBase64.Length > 12 * 1024 * 1024) throw new ArgumentException("Resource byte limit exceeded.");
        ValidationBinding.RequireDigest(sha256);
        Key = key; Category = category; RelativePath = relativePath; MediaType = mediaType; Length = length; Sha256 = sha256; ContentBase64 = contentBase64;
        _ = ReadBytes();
    }
    public string Key { get; }
    public string Category { get; }
    public string RelativePath { get; }
    public string MediaType { get; }
    public long Length { get; }
    public string Sha256 { get; }
    public string ContentBase64 { get; }
    public byte[] ReadBytes()
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(ContentBase64); } catch (FormatException error) { throw new InvalidDataException("Snapshot resource encoding changed.", error); }
        if (bytes.LongLength != Length || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot resource bytes failed integrity verification.");
        return bytes;
    }
    public static StandardsSnapshotResource Create(string key, string category, string relativePath, string mediaType, byte[] bytes) =>
        new(key, category, relativePath, mediaType, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Convert.ToBase64String(bytes));
}

public sealed class StandardsProfileSnapshot
{
    [JsonConstructor]
    public StandardsProfileSnapshot(string id, int schemaVersion, StandardsSnapshotDescriptor descriptor, IReadOnlyList<StandardsSnapshotResource> resources, DateTime acquiredUtc)
    {
        if (schemaVersion != 1 || acquiredUtc.Kind != DateTimeKind.Utc || acquiredUtc == default) throw new InvalidDataException("Unsupported snapshot schema or acquisition date.");
        if (descriptor is null || resources is null || descriptor.Provenance is null || descriptor.Scopes is null || descriptor.Evidence is null ||
            descriptor.Lifecycle is null || descriptor.Provenance.Any(source => source is null) || descriptor.Scopes.Any(scope => scope is null) || descriptor.Evidence.Any(evidence => evidence is null) || resources.Any(resource => resource is null))
            throw new InvalidDataException("Snapshot inventories cannot contain null metadata or entries.");
        Descriptor = descriptor with { Provenance = Array.AsReadOnly(descriptor.Provenance.ToArray()), Scopes = Array.AsReadOnly(descriptor.Scopes.ToArray()), Evidence = Array.AsReadOnly(descriptor.Evidence.ToArray()) };
        Resources = Array.AsReadOnly(resources.OrderBy(resource => resource.Key, StringComparer.Ordinal).ToArray());
        Id = id; SchemaVersion = schemaVersion; AcquiredUtc = acquiredUtc; Verify();
    }
    public string Id { get; }
    public int SchemaVersion { get; }
    public StandardsSnapshotDescriptor Descriptor { get; }
    public IReadOnlyList<StandardsSnapshotResource> Resources { get; }
    public DateTime AcquiredUtc { get; }
    [JsonIgnore] public string TemplateKey => Descriptor.TemplateKey;
    [JsonIgnore] public string RuleProfileAlias => Descriptor.RuleProfileAlias;
    [JsonIgnore] public StandardsSnapshotLifecycle Lifecycle => Descriptor.Lifecycle;
    public static StandardsProfileSnapshot Create(StandardsSnapshotDescriptor descriptor, IEnumerable<StandardsSnapshotResource> resources, DateTime acquiredUtc)
    {
        var copy = resources.OrderBy(resource => resource.Key, StringComparer.Ordinal).ToArray();
        return new(ComputeId(descriptor, copy), 1, descriptor, copy, acquiredUtc);
    }
    public void Verify()
    {
        if (Resources.Count is < 1 or > 64 || Resources.Sum(resource => resource.Length) > 32 * 1024 * 1024 || Resources.Select(resource => resource.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Resources.Count ||
            Resources.Select(resource => resource.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Resources.Count ||
            Id != ComputeId(Descriptor, Resources)) throw new InvalidDataException("Snapshot content identity failed verification.");
        foreach (var resource in Resources) _ = resource.ReadBytes();
        var orderedPaths = Resources.Select(resource => resource.RelativePath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (orderedPaths.Where((path, index) => index > 0 && path.StartsWith(orderedPaths[index - 1] + "/", StringComparison.OrdinalIgnoreCase)).Any())
            throw new InvalidDataException("A standards resource cannot also be a directory prefix for another resource.");
        foreach (var text in new[] { Descriptor.TemplateKey, Descriptor.Region, Descriptor.IchEctdVersion, Descriptor.RuleProfileAlias, Descriptor.Lifecycle.RetirementPolicy })
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Snapshot metadata is incomplete.");
        if (!Enum.IsDefined(Lifecycle.Status) || Lifecycle.RetiredFrom < Lifecycle.EffectiveFrom) throw new InvalidDataException("Snapshot lifecycle is invalid.");
        if (Descriptor.Provenance.Count > 64 || Descriptor.Evidence.Count > 64 || Descriptor.Scopes.Count > 1000 ||
            Descriptor.Provenance.Select(source => source.SourceUrl).Distinct(StringComparer.Ordinal).Count() != Descriptor.Provenance.Count ||
            Descriptor.Evidence.Select(evidence => evidence.Category).Distinct(StringComparer.Ordinal).Count() != Descriptor.Evidence.Count)
            throw new InvalidDataException("Snapshot evidence inventories exceed bounds or contain duplicates.");
        foreach (var source in Descriptor.Provenance)
        {
            if (!Uri.TryCreate(source.SourceUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(source.Version) || source.VerificationStatus is not ("Verified" or "Unverified") ||
                source.VerifiedOn > DateOnly.FromDateTime(AcquiredUtc) || source.AcquiredOn > DateOnly.FromDateTime(AcquiredUtc) || source.AcquiredOn > source.VerifiedOn)
                throw new InvalidDataException("Snapshot source provenance is malformed or has inconsistent dates.");
            if (source.Sha256 is not null && !Digest(source.Sha256)) throw new InvalidDataException("Source provenance requires a SHA-256 digest.");
            if (source.VerificationStatus == "Verified" && (source.Sha256 is null || source.VerifiedOn is null || source.AcquiredOn is null || !Resources.Any(resource => resource.Sha256 == source.Sha256 && resource.Category == "OfficialSource" && resource.Length > 0)))
                throw new InvalidDataException("Verified official provenance requires captured source bytes and acquisition/verification dates.");
        }
        foreach (var evidence in Descriptor.Evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence.Category) || string.IsNullOrWhiteSpace(evidence.Reason) || evidence.Reason.Length > 4096 ||
                evidence.Status is not ("Verified" or "Unverified" or "Missing") || evidence.EvidenceSha256 is not null && !Digest(evidence.EvidenceSha256))
                throw new InvalidDataException("Snapshot evidence is malformed.");
            if (evidence.Status == "Verified" && (evidence.EvidenceSha256 is null || !Resources.Any(resource => resource.Sha256 == evidence.EvidenceSha256 && resource.Category == evidence.Category && resource.Length > 0)))
                throw new InvalidDataException("Verified evidence must name captured resource bytes.");
        }
        foreach (var scope in Descriptor.Scopes)
            if (new[] { scope.ApplicationType, scope.ProcedureType, scope.Country, scope.Language, scope.SectionScope, scope.Operation, scope.EvidenceStatus }.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Business scopes must explicitly declare every dimension.");
    }
    private static string ComputeId(StandardsSnapshotDescriptor descriptor, IEnumerable<StandardsSnapshotResource> resources) => "sha256:" +
        CanonicalJson.Digest(JsonSerializer.SerializeToElement(new { SchemaVersion = 1, Descriptor = descriptor,
            Resources = resources.OrderBy(resource => resource.Key, StringComparer.Ordinal).Select(resource => new { resource.Key, resource.Category, resource.RelativePath, resource.MediaType, resource.Length, resource.Sha256 }) }));
    public IReadOnlyList<string> Evaluate(StandardsBusinessContext? context, DateOnly selectionDate, string templateKey)
    {
        Verify(); var reasons = new List<string>();
        if (templateKey != TemplateKey) reasons.Add("TemplateMismatch: The snapshot does not belong to this application template.");
        if (Lifecycle.Status != StandardsSnapshotStatus.Active) reasons.Add("ProfileNotActive: " + Lifecycle.Status);
        if (selectionDate < Lifecycle.EffectiveFrom || Lifecycle.RetiredFrom is { } retired && selectionDate >= retired) reasons.Add("ProfileDateNotApplicable: Selection date is outside the effective interval.");
        if (context is null) reasons.Add("BusinessContextMissing: An explicit confirmed business selection is required.");
        else if (!Descriptor.Scopes.Any(scope => scope.Finalize && scope.EvidenceStatus == "Verified" && scope.Matches(context))) reasons.Add("BusinessScopeNotQualified: No verified formal scope matches the selection.");
        foreach (var category in new[] { "Sources", "ControlledVocabulary", "MetadataMapping", "Writer", "Rules", "ExternalComparison" })
            if (!Descriptor.Evidence.Any(evidence => evidence.Category == category && evidence.Status == "Verified" && evidence.EvidenceSha256 is not null))
                reasons.Add("EvidenceIncomplete: " + category);
        if (Descriptor.Provenance.Count == 0 || Descriptor.Provenance.Any(source => source.VerificationStatus != "Verified" || source.VerifiedOn is null || source.Sha256 is null))
            reasons.Add("SourceVerificationIncomplete: Versioned official provenance has not been verified.");
        if (Descriptor.Provenance.Any(source => source.VerifiedOn > selectionDate)) reasons.Add("SourceVerificationDate: Verification occurred after the bound selection date.");
        foreach (var category in new[] { "DTD", "XSL", "ControlledVocabulary", "NodeSchema", "RuleMapping", "XmlAssetManifest" })
            if (!Resources.Any(resource => resource.Category == category && resource.Length > 0)) reasons.Add("RequiredResourceMissing: " + category);
        return reasons.AsReadOnly();
    }
    private static bool Digest(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
