using System.Text.Json;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Domain.Finalization;

// The role a captured file has in the release. Only the validation report is evidence
// beside the payload; everything the operator delivers lives under payload/.
public enum ReleaseFileKind { Payload, ValidationReport }

// One immutable file of a captured release. RelativePath is release-relative: delivered
// files live under "payload/", the report sits beside the manifest. Length and both hashes
// were computed from the bytes actually written, not from the mutable workspace.
public sealed record ReleaseManifestFile(string RelativePath, long Length, string Sha256, string Md5,
    ReleaseFileKind Kind);

// A standards asset the content was validated against, recorded by its bundle path and
// pinned hash. When the same path is delivered the captured bytes must match.
public sealed record ReleaseManifestAsset(string Key, string Category, string Version, string Path, string Sha256);

// The exact validation evidence the captured content carries. The run's database id is
// deliberately absent: the archived report bytes and the binding digests identify the
// evidence, so identical packages stay one content identity even when a retry created a
// second run.
public sealed record ReleaseValidationReference(string ReportSha256, string ValidationMode,
    string StandardsProfileSnapshotId, string IchEctdVersion, string? RegionalModule1Version, string RulesDigest,
    string EngineVersion, string? ComparisonInputDigest);

// One selected historical version from the archived baseline: the exact earlier content
// the validated package referred to. Finalized sources keep their snapshot identity so a
// release can name which delivered version it builds on.
public sealed record ReleaseHistoryReference(string SequenceNumber, string SourceKind, string SourceId,
    string ContentDigest, string ProfileSnapshotId, string TrustStatus, Guid? ValidationRunId,
    Guid? FinalizedSequenceId);

// A lifecycle edge addressed exactly like a backbone modified-file reference: the source
// leaf plus the earlier sequence/backbone/leaf it modifies, and - when the archived
// baseline selected that sequence - the exact historical content digest.
public sealed record ReleaseLifecycleEdge(string SourceLeafId, string Operation, string TargetSequenceNumber,
    string TargetBackboneRelativePath, string TargetLeafId, string? TargetContentDigest);

// One leaf of the captured sequence: the immutable document behind it, the physical file
// it declares and its ordering. Delete leaves deliver no file but keep their evidence.
public sealed record ReleaseManifestLeaf(Guid PlacementId, Guid DocumentId, string LeafId, string CtdSection,
    string Operation, string Href, Guid? NodeInstanceId, int SortOrder);

// One node of the captured sequence, so the manifest describes the content without the
// mutable workspace.
public sealed record ReleaseManifestNode(Guid NodeInstanceId, Guid? ParentInstanceId, string DefinitionKey,
    string DefinitionVersion, string? Title, int SortOrder, string MetadataStatus,
    IReadOnlyDictionary<string, string> Attributes);

// P4-02/P4-03: the deterministic manifest of one captured release.
//
// Digest identifies the delivered bytes plus the standards, history and validation facts
// they were validated against. It excludes the workspace revision and tool version - facts
// that vary between two captures of the same content - so identical validated content
// always yields one identity and therefore one snapshot. Those excluded facts stay
// authoritative on the finalized snapshot record and in this document.
//
// Every read recomputes the digest from the stored file, so a manifest can never silently
// describe different bytes than the snapshot recorded.
public sealed class ReleaseManifest
{
    public const int CurrentSchemaVersion = 1;
    private const int MaxEntries = 100_000;

    public ReleaseManifest(Guid applicationId, string sequenceNumber, long sourceWorkspaceRevision,
        string createdByToolVersion, string validatedInputManifestDigest, JsonElement historyBaseline,
        string historyBaselineDigest, ReleaseValidationReference validation, IEnumerable<ReleaseManifestAsset> assets,
        IEnumerable<ReleaseManifestFile> files, IEnumerable<ReleaseLifecycleEdge> lifecycle,
        IEnumerable<ReleaseManifestLeaf> leaves, IEnumerable<ReleaseManifestNode> nodes)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        PackageInputManifest.RequireSequenceNumber(sequenceNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceWorkspaceRevision);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sourceWorkspaceRevision, 9007199254740991L);
        FinalizationFields.RequireText(createdByToolVersion, 64, nameof(createdByToolVersion));
        ValidationBinding.RequireDigest(validatedInputManifestDigest);
        ValidationBinding.RequireDigest(historyBaselineDigest);
        ArgumentNullException.ThrowIfNull(validation);
        var references = ReadBaseline(historyBaseline, applicationId, sequenceNumber, historyBaselineDigest);

        var assetCopy = assets.ToArray();
        var fileCopy = files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
        var edgeCopy = lifecycle.OrderBy(edge => edge.SourceLeafId, StringComparer.Ordinal).ToArray();
        var leafCopy = leaves.OrderBy(leaf => leaf.CtdSection, StringComparer.Ordinal).ThenBy(leaf => leaf.SortOrder)
            .ThenBy(leaf => leaf.PlacementId).ToArray();
        var nodeCopy = nodes.OrderBy(node => node.SortOrder).ThenBy(node => node.NodeInstanceId).ToArray();
        if (assetCopy.Any(asset => asset is null) || fileCopy.Any(file => file is null) || edgeCopy.Any(edge => edge is null) ||
            leafCopy.Any(leaf => leaf is null) || nodeCopy.Any(node => node is null))
            throw new ArgumentException("Manifest entries cannot be null.");
        if (fileCopy.Length > MaxEntries || assetCopy.Length > MaxEntries || edgeCopy.Length > MaxEntries ||
            leafCopy.Length > MaxEntries || nodeCopy.Length > MaxEntries)
            throw new ArgumentException("The captured release exceeds the supported manifest size.", nameof(files));

        foreach (var asset in assetCopy)
        {
            FinalizationFields.RequireText(asset.Key, 128, nameof(assets));
            FinalizationFields.RequireText(asset.Category, 32, nameof(assets));
            FinalizationFields.RequireText(asset.Version, 64, nameof(assets));
            RequireRelativePath(asset.Path, nameof(assets));
            ValidationBinding.RequireDigest(asset.Sha256);
        }
        foreach (var file in fileCopy)
        {
            RequireRelativePath(file.RelativePath, nameof(files));
            ArgumentOutOfRangeException.ThrowIfNegative(file.Length);
            ValidationBinding.RequireDigest(file.Sha256);
            RequireMd5(file.Md5);
            if (!Enum.IsDefined(file.Kind)) throw new ArgumentException("Unknown release file kind.", nameof(files));
        }
        foreach (var edge in edgeCopy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.SourceLeafId);
            FinalizationFields.RequireText(edge.Operation, 16, nameof(lifecycle));
            PackageInputManifest.RequireSequenceNumber(edge.TargetSequenceNumber);
            if (string.CompareOrdinal(edge.TargetSequenceNumber, sequenceNumber) >= 0)
                throw new ArgumentException("Lifecycle targets must be in an earlier sequence.", nameof(lifecycle));
            RequireRelativePath(edge.TargetBackboneRelativePath, nameof(lifecycle));
            if (!edge.TargetBackboneRelativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Lifecycle targets address an XML backbone.", nameof(lifecycle));
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.TargetLeafId);
            var selected = references.SingleOrDefault(reference => reference.SequenceNumber == edge.TargetSequenceNumber);
            // The exactness claim: a lifecycle target either names the selected historical
            // content or admits that no baseline covers it. A digest that disagrees with the
            // archived baseline would make the reference unverifiable.
            if (selected is null)
            {
                if (edge.TargetContentDigest is not null)
                    throw new ArgumentException(
                        $"Lifecycle target sequence {edge.TargetSequenceNumber} has no selected baseline content to name.",
                        nameof(lifecycle));
            }
            else if (!string.Equals(selected.ContentDigest, edge.TargetContentDigest, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Lifecycle target sequence {edge.TargetSequenceNumber} does not name the selected historical content.",
                    nameof(lifecycle));
        }
        foreach (var leaf in leafCopy)
        {
            if (leaf.PlacementId == Guid.Empty || leaf.DocumentId == Guid.Empty)
                throw new ArgumentException("A leaf requires its placement and document identity.", nameof(leaves));
            FinalizationFields.RequireText(leaf.LeafId, 256, nameof(leaves));
            FinalizationFields.RequireText(leaf.CtdSection, 64, nameof(leaves));
            FinalizationFields.RequireText(leaf.Operation, 16, nameof(leaves));
            RequireRelativePath(leaf.Href, nameof(leaves));
            ArgumentOutOfRangeException.ThrowIfNegative(leaf.SortOrder);
        }
        foreach (var node in nodeCopy)
        {
            if (node.NodeInstanceId == Guid.Empty) throw new ArgumentException("A node requires its instance identity.", nameof(nodes));
            FinalizationFields.RequireText(node.DefinitionKey, 128, nameof(nodes));
            FinalizationFields.RequireText(node.DefinitionVersion, 64, nameof(nodes));
            FinalizationFields.RequireText(node.MetadataStatus, 64, nameof(nodes));
            ArgumentOutOfRangeException.ThrowIfNegative(node.SortOrder);
            if (node.Attributes.Any(attribute => string.IsNullOrWhiteSpace(attribute.Key)))
                throw new ArgumentException("Node attributes require nonblank names.", nameof(nodes));
        }
        // A case-insensitive collision is still one file on Windows; two manifest entries
        // would make the captured bytes ambiguous.
        if (fileCopy.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fileCopy.Length)
            throw new ArgumentException("A release path may appear only once, including case-insensitive collisions.", nameof(files));
        if (assetCopy.Select(asset => asset.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != assetCopy.Length)
            throw new ArgumentException("A standards asset path may appear only once.", nameof(assets));
        if (edgeCopy.Select(edge => edge.SourceLeafId).Distinct(StringComparer.Ordinal).Count() != edgeCopy.Length)
            throw new ArgumentException("A leaf may have at most one lifecycle operation.", nameof(lifecycle));
        if (leafCopy.Select(leaf => leaf.PlacementId).Distinct().Count() != leafCopy.Length)
            throw new ArgumentException("A placement may appear only once.", nameof(leaves));
        if (nodeCopy.Select(node => node.NodeInstanceId).Distinct().Count() != nodeCopy.Length)
            throw new ArgumentException("A node instance may appear only once.", nameof(nodes));
        if (fileCopy.Count(file => file.Kind == ReleaseFileKind.ValidationReport) != 1)
            throw new ArgumentException("A captured release contains exactly one validation report.", nameof(files));
        if (!fileCopy.Any(file => file.Kind == ReleaseFileKind.Payload))
            throw new ArgumentException("A captured release contains at least one delivered file.", nameof(files));
        // A declared standards asset that is also delivered must be those exact bytes; a
        // locally modified DTD would otherwise ship under a manifest claiming otherwise.
        var delivered = fileCopy.Where(file => file.Kind == ReleaseFileKind.Payload)
            .ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assetCopy)
        {
            if (delivered.TryGetValue(asset.Path, out var file) && !string.Equals(file.Sha256, asset.Sha256, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Delivered standards asset '{asset.Key}' does not match its pinned bytes.", nameof(assets));
        }
        // The leaves and nodes are the content's own description. They must agree with the
        // delivered files, or the manifest would claim a document the release does not
        // contain and a lifecycle edge no delivered leaf explains.
        var deliveredPaths = new HashSet<string>(delivered.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in leafCopy)
        {
            if (leaf.Operation == "delete")
            {
                // A delete leaf declares only a lifecycle operation; it delivers no file.
                if (deliveredPaths.Contains("payload/" + sequenceNumber + "/" + leaf.Href))
                    throw new ArgumentException(
                        $"Delete leaf '{leaf.LeafId}' must not deliver a file.", nameof(leaves));
                continue;
            }
            if (!deliveredPaths.Contains("payload/" + sequenceNumber + "/" + leaf.Href))
                throw new ArgumentException(
                    $"Leaf '{leaf.LeafId}' names a file the release does not deliver.", nameof(leaves));
        }
        var nodeIds = nodeCopy.Select(node => node.NodeInstanceId).ToHashSet();
        foreach (var leaf in leafCopy)
        {
            if (leaf.NodeInstanceId is { } nodeId && !nodeIds.Contains(nodeId))
                throw new ArgumentException(
                    $"Leaf '{leaf.LeafId}' names a node the release does not describe.", nameof(leaves));
        }
        foreach (var node in nodeCopy)
        {
            if (node.ParentInstanceId is { } parent && !nodeIds.Contains(parent))
                throw new ArgumentException(
                    $"Node '{node.NodeInstanceId}' names a parent the release does not describe.", nameof(nodes));
        }
        var leafIds = leafCopy.Select(leaf => leaf.LeafId).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in edgeCopy)
        {
            if (!leafIds.Contains(edge.SourceLeafId))
                throw new ArgumentException(
                    $"Lifecycle edge '{edge.SourceLeafId}' is not a leaf of this release.", nameof(lifecycle));
        }

        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber;
        SourceWorkspaceRevision = sourceWorkspaceRevision;
        CreatedByToolVersion = createdByToolVersion.Trim();
        ValidatedInputManifestDigest = validatedInputManifestDigest;
        HistoryBaselineDigest = historyBaselineDigest;
        HistoryBaseline = historyBaseline.Clone();
        HistoryReferences = Array.AsReadOnly(references.OrderBy(reference => reference.SequenceNumber, StringComparer.Ordinal).ToArray());
        Validation = validation;
        Assets = Array.AsReadOnly(assetCopy.OrderBy(asset => asset.Key, StringComparer.Ordinal).ToArray());
        Files = Array.AsReadOnly(fileCopy);
        Lifecycle = Array.AsReadOnly(edgeCopy);
        Leaves = Array.AsReadOnly(leafCopy);
        Nodes = Array.AsReadOnly(nodeCopy);
        Digest = CanonicalJson.Digest(ToContentElement());
    }

    public int SchemaVersion { get; } = CurrentSchemaVersion;
    public Guid ApplicationId { get; }
    public string SequenceNumber { get; }
    public long SourceWorkspaceRevision { get; }
    public string CreatedByToolVersion { get; }
    // The validated package these bytes are: the run's bound input digest (the combined
    // directory/ZIP digest when a comparison was validated).
    public string ValidatedInputManifestDigest { get; }
    public string HistoryBaselineDigest { get; }
    public JsonElement HistoryBaseline { get; }
    public IReadOnlyList<ReleaseHistoryReference> HistoryReferences { get; }
    public ReleaseValidationReference Validation { get; }
    public IReadOnlyList<ReleaseManifestAsset> Assets { get; }
    public IReadOnlyList<ReleaseManifestFile> Files { get; }
    public IReadOnlyList<ReleaseLifecycleEdge> Lifecycle { get; }
    public IReadOnlyList<ReleaseManifestLeaf> Leaves { get; }
    public IReadOnlyList<ReleaseManifestNode> Nodes { get; }
    // Identity of the captured content: recorded by the finalized snapshot and used as the
    // release directory name. Recomputable from this file alone.
    public string Digest { get; }

    public JsonElement ToElement() => JsonSerializer.SerializeToElement(new
    {
        schemaVersion = SchemaVersion,
        sourceWorkspaceRevision = SourceWorkspaceRevision,
        createdByToolVersion = CreatedByToolVersion,
        digest = Digest,
        content = ToContentElement()
    });

    private JsonElement ToContentElement() => JsonSerializer.SerializeToElement(new
    {
        applicationId = ApplicationId.ToString("D"),
        sequenceNumber = SequenceNumber,
        validatedInputManifestDigest = ValidatedInputManifestDigest,
        historyBaseline = HistoryBaseline,
        validation = new
        {
            reportSha256 = Validation.ReportSha256,
            validationMode = Validation.ValidationMode,
            standardsProfileSnapshotId = Validation.StandardsProfileSnapshotId,
            ichEctdVersion = Validation.IchEctdVersion,
            regionalModule1Version = Validation.RegionalModule1Version,
            rulesDigest = Validation.RulesDigest,
            engineVersion = Validation.EngineVersion,
            comparisonInputDigest = Validation.ComparisonInputDigest
        },
        assets = Assets.Select(asset => new
        {
            key = asset.Key, category = asset.Category, version = asset.Version,
            path = asset.Path, sha256 = asset.Sha256
        }),
        files = Files.Select(file => new
        {
            relativePath = file.RelativePath, length = file.Length, sha256 = file.Sha256,
            md5 = file.Md5, kind = file.Kind.ToString()
        }),
        lifecycle = Lifecycle.Select(edge => new
        {
            sourceLeafId = edge.SourceLeafId, operation = edge.Operation,
            targetSequenceNumber = edge.TargetSequenceNumber,
            targetBackboneRelativePath = edge.TargetBackboneRelativePath, targetLeafId = edge.TargetLeafId,
            targetContentDigest = edge.TargetContentDigest
        }),
        leaves = Leaves.Select(leaf => new
        {
            placementId = leaf.PlacementId.ToString("D"), documentId = leaf.DocumentId.ToString("D"),
            leafId = leaf.LeafId, ctdSection = leaf.CtdSection, operation = leaf.Operation, href = leaf.Href,
            nodeInstanceId = leaf.NodeInstanceId?.ToString("D"), sortOrder = leaf.SortOrder
        }),
        nodes = Nodes.Select(node => new
        {
            nodeInstanceId = node.NodeInstanceId.ToString("D"),
            parentInstanceId = node.ParentInstanceId?.ToString("D"),
            definitionKey = node.DefinitionKey, definitionVersion = node.DefinitionVersion,
            title = node.Title, sortOrder = node.SortOrder, metadataStatus = node.MetadataStatus,
            attributes = node.Attributes.OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
                .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal)
        })
    });

    // The bytes written to manifest.json. Canonical encoding sorts object keys and keeps
    // the validated array order, so the same captured content always produces one file.
    public byte[] ToUtf8Json() => CanonicalJson.Encode(ToElement());

    public static ReleaseManifest FromJson(ReadOnlySpan<byte> utf8Json, Guid expectedApplicationId,
        string expectedSequence, string expectedDigest)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());
        return FromElement(document.RootElement, expectedApplicationId, expectedSequence, expectedDigest);
    }

    // Re-reads a stored manifest and proves it is the manifest its snapshot recorded:
    // same application/sequence, same committed digest, every claim still valid.
    public static ReleaseManifest FromElement(JsonElement element, Guid expectedApplicationId, string expectedSequence,
        string expectedDigest)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The stored release manifest is not a JSON object.");
        if (element.GetProperty("schemaVersion").GetInt32() != CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported release manifest schema.");
        ReleaseManifest manifest;
        try
        {
            var content = element.GetProperty("content");
            var validation = content.GetProperty("validation");
            var regional = validation.GetProperty("regionalModule1Version");
            var comparison = validation.GetProperty("comparisonInputDigest");
            manifest = new ReleaseManifest(
                content.GetProperty("applicationId").GetGuid(),
                content.GetProperty("sequenceNumber").GetString() ?? throw new InvalidDataException("Missing sequence number."),
                element.GetProperty("sourceWorkspaceRevision").GetInt64(),
                element.GetProperty("createdByToolVersion").GetString() ?? throw new InvalidDataException("Missing tool version."),
                content.GetProperty("validatedInputManifestDigest").GetString() ?? throw new InvalidDataException("Missing input digest."),
                content.GetProperty("historyBaseline"),
                ReadBaselineDigest(content.GetProperty("historyBaseline")),
                new ReleaseValidationReference(
                    validation.GetProperty("reportSha256").GetString() ?? throw new InvalidDataException("Missing report digest."),
                    validation.GetProperty("validationMode").GetString() ?? throw new InvalidDataException("Missing validation mode."),
                    validation.GetProperty("standardsProfileSnapshotId").GetString() ?? throw new InvalidDataException("Missing profile."),
                    validation.GetProperty("ichEctdVersion").GetString() ?? throw new InvalidDataException("Missing ICH version."),
                    regional.ValueKind == JsonValueKind.Null ? null : regional.GetString(),
                    validation.GetProperty("rulesDigest").GetString() ?? throw new InvalidDataException("Missing rules digest."),
                    validation.GetProperty("engineVersion").GetString() ?? throw new InvalidDataException("Missing engine version."),
                    comparison.ValueKind == JsonValueKind.Null ? null : comparison.GetString()),
                content.GetProperty("assets").EnumerateArray().Select(asset => new ReleaseManifestAsset(
                    asset.GetProperty("key").GetString()!, asset.GetProperty("category").GetString()!,
                    asset.GetProperty("version").GetString()!, asset.GetProperty("path").GetString()!,
                    asset.GetProperty("sha256").GetString()!)).ToArray(),
                content.GetProperty("files").EnumerateArray().Select(file => new ReleaseManifestFile(
                    file.GetProperty("relativePath").GetString()!, file.GetProperty("length").GetInt64(),
                    file.GetProperty("sha256").GetString()!, file.GetProperty("md5").GetString()!,
                    Enum.Parse<ReleaseFileKind>(file.GetProperty("kind").GetString()!))).ToArray(),
                content.GetProperty("lifecycle").EnumerateArray().Select(edge =>
                {
                    var digest = edge.GetProperty("targetContentDigest");
                    return new ReleaseLifecycleEdge(edge.GetProperty("sourceLeafId").GetString()!,
                        edge.GetProperty("operation").GetString()!, edge.GetProperty("targetSequenceNumber").GetString()!,
                        edge.GetProperty("targetBackboneRelativePath").GetString()!,
                        edge.GetProperty("targetLeafId").GetString()!,
                        digest.ValueKind == JsonValueKind.Null ? null : digest.GetString());
                }).ToArray(),
                content.GetProperty("leaves").EnumerateArray().Select(leaf =>
                {
                    var node = leaf.GetProperty("nodeInstanceId");
                    return new ReleaseManifestLeaf(leaf.GetProperty("placementId").GetGuid(),
                        leaf.GetProperty("documentId").GetGuid(), leaf.GetProperty("leafId").GetString()!,
                        leaf.GetProperty("ctdSection").GetString()!, leaf.GetProperty("operation").GetString()!,
                        leaf.GetProperty("href").GetString()!,
                        node.ValueKind == JsonValueKind.Null ? null : node.GetGuid(),
                        leaf.GetProperty("sortOrder").GetInt32());
                }).ToArray(),
                content.GetProperty("nodes").EnumerateArray().Select(node =>
                {
                    var parent = node.GetProperty("parentInstanceId");
                    var title = node.GetProperty("title");
                    return new ReleaseManifestNode(node.GetProperty("nodeInstanceId").GetGuid(),
                        parent.ValueKind == JsonValueKind.Null ? null : parent.GetGuid(),
                        node.GetProperty("definitionKey").GetString()!, node.GetProperty("definitionVersion").GetString()!,
                        title.ValueKind == JsonValueKind.Null ? null : title.GetString(),
                        node.GetProperty("sortOrder").GetInt32(), node.GetProperty("metadataStatus").GetString()!,
                        node.GetProperty("attributes").EnumerateObject()
                            .ToDictionary(attribute => attribute.Name,
                                attribute => attribute.Value.GetString() ?? string.Empty, StringComparer.Ordinal));
                }).ToArray());
            // The file's own recorded digest must agree with the recomputed one; a manifest
            // edited after capture cannot pass even if the outer digest was updated too.
            if (!string.Equals(element.GetProperty("digest").GetString(), manifest.Digest, StringComparison.Ordinal))
                throw new InvalidDataException("The stored release manifest digest is inconsistent.");
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or
            FormatException or ArgumentException)
        {
            throw new InvalidDataException("The stored release manifest is malformed.", exception);
        }
        if (manifest.ApplicationId != expectedApplicationId ||
            !string.Equals(manifest.SequenceNumber, expectedSequence, StringComparison.Ordinal))
            throw new InvalidDataException("The stored release manifest belongs to a different sequence.");
        if (!string.Equals(manifest.Digest, expectedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The stored release manifest differs from its committed digest.");
        return manifest;
    }

    private static string ReadBaselineDigest(JsonElement baseline) =>
        baseline.ValueKind == JsonValueKind.Object && baseline.TryGetProperty("digest", out var digest) &&
        digest.ValueKind == JsonValueKind.String
            ? digest.GetString() ?? throw new InvalidDataException("Missing baseline digest.")
            : throw new InvalidDataException("The stored history baseline has no digest.");

    // The archived history baseline is stored verbatim; its selected entries are the exact
    // historical content the validated package referred to. The recorded digest must be
    // the one the validation run bound, so a baseline cannot be swapped for another.
    private static ReleaseHistoryReference[] ReadBaseline(JsonElement baseline, Guid applicationId,
        string sequenceNumber, string historyBaselineDigest)
    {
        if (baseline.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The history baseline must be the archived baseline manifest.", nameof(baseline));
        try
        {
            if (baseline.GetProperty("applicationId").GetGuid() != applicationId ||
                !string.Equals(baseline.GetProperty("targetSequence").GetString(), sequenceNumber, StringComparison.Ordinal))
                throw new ArgumentException("The archived history baseline belongs to a different application or sequence.", nameof(baseline));
            if (!string.Equals(ReadBaselineDigest(baseline), historyBaselineDigest, StringComparison.Ordinal))
                throw new ArgumentException("The archived history baseline is not the one this validation run bound.", nameof(baseline));
            var references = baseline.GetProperty("entries").EnumerateArray().Select(entry =>
            {
                var runId = entry.GetProperty("validationRunId");
                var snapshotId = entry.GetProperty("finalizedSequenceId");
                var value = new ReleaseHistoryReference(entry.GetProperty("sequenceNumber").GetString()!,
                    entry.GetProperty("sourceKind").GetString()!, entry.GetProperty("sourceId").GetString()!,
                    entry.GetProperty("contentDigest").GetString()!, entry.GetProperty("profileSnapshotId").GetString()!,
                    entry.GetProperty("trustStatus").GetString()!,
                    runId.ValueKind == JsonValueKind.Null ? null : runId.GetGuid(),
                    snapshotId.ValueKind == JsonValueKind.Null ? null : snapshotId.GetGuid());
                PackageInputManifest.RequireSequenceNumber(value.SequenceNumber);
                if (string.CompareOrdinal(value.SequenceNumber, sequenceNumber) >= 0)
                    throw new ArgumentException("Historical references must be earlier sequences.", nameof(baseline));
                ValidationBinding.RequireDigest(value.ContentDigest);
                ArgumentException.ThrowIfNullOrWhiteSpace(value.SourceId);
                ArgumentException.ThrowIfNullOrWhiteSpace(value.SourceKind);
                ArgumentException.ThrowIfNullOrWhiteSpace(value.ProfileSnapshotId);
                ArgumentException.ThrowIfNullOrWhiteSpace(value.TrustStatus);
                return value;
            }).ToArray();
            if (references.Select(reference => reference.SequenceNumber).Distinct(StringComparer.Ordinal).Count() != references.Length)
                throw new ArgumentException("The baseline selects each historical sequence once.", nameof(baseline));
            return references;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ArgumentException("The archived history baseline is malformed.", nameof(baseline), exception);
        }
    }

    // Manifest paths are relative to the release directory and are never resolved from
    // caller input; they must already be canonical portable segments.
    public static void RequireRelativePath(string? value, string parameterName)
    {
        if (value is null || value.Length is 0 or > 1024 || value.Contains('\\') || value.StartsWith('/') || value.EndsWith('/'))
            throw new ArgumentException("Manifest paths must be portable relative paths.", parameterName);
        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment != PortablePathSegment.NormalizeAndValidate(segment, parameterName))
                throw new ArgumentException("Manifest paths must retain exact portable segments.", parameterName);
        }
    }

    private static void RequireMd5(string md5)
    {
        if (md5.Length != 32 || md5.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("An MD5 digest must contain 32 lowercase hexadecimal characters.", nameof(md5));
    }
}
