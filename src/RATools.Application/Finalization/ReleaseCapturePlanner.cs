using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Standards;
using RATools.Domain.Common;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.Finalization;

// The planned capture plus what the manifest needs. ValidatedDocuments maps a validation
// logical path (sequence/href) to the digest the validation run actually captured, so the
// capture service can prove it is delivering those bytes and not merely today's workspace.
// Lifecycle, leaves and nodes are the content's own facts, recorded so the manifest
// describes the sequence without re-reading the mutable workspace.
public sealed record ReleaseCapturePreparation(ReleaseCapturePlan Capture, IReadOnlyList<ReleaseManifestAsset> Assets,
    ReleaseValidationReference Validation, JsonElement HistoryBaseline,
    IReadOnlyDictionary<string, string> ValidatedDocuments, IReadOnlyList<ReleaseLifecycleEdge> Lifecycle,
    IReadOnlyList<ReleaseManifestLeaf> Leaves, IReadOnlyList<ReleaseManifestNode> Nodes);

// Builds the exact deliverable from the workspace. It runs under the application lock the
// caller holds, so the captured revision is the one that was checked.
public interface IReleaseCapturePlanner
{
    Task<ReleaseCapturePreparation> PlanAsync(FinalizeSequenceRequest request, PackageValidationRun run,
        StoredPackageValidationRequest storedRequest, CancellationToken cancellationToken = default);
}

public sealed class EctdReleaseCapturePlanner(
    IEctdPackageModelBuilder packageModelBuilder,
    IIchIndexXmlWriter ichIndexXmlWriter,
    IRegionalBackboneWriterRegistry regionalBackboneWriterRegistry,
    IStandardsProfileProvider standardsProfileProvider) : IReleaseCapturePlanner
{
    public async Task<ReleaseCapturePreparation> PlanAsync(FinalizeSequenceRequest request, PackageValidationRun run,
        StoredPackageValidationRequest storedRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(storedRequest);

        var package = await packageModelBuilder.BuildAsync(
            new BuildEctdPackageRequest(request.ApplicationId, request.SequenceNumber.Trim()), cancellationToken)
            .ConfigureAwait(false);
        var profile = standardsProfileProvider.GetProfile(package.Application.TemplateKey);
        // The standards versions are properties of the template the content was built
        // from. A request that claims different versions must not be recorded as if the
        // package had been captured against them.
        var requestedRegional = string.IsNullOrWhiteSpace(request.RegionalModule1Version)
            ? null : request.RegionalModule1Version.Trim();
        if (!string.Equals(request.IchEctdVersion.Trim(), profile.IchEctdVersion, StringComparison.Ordinal) ||
            !string.Equals(requestedRegional, profile.UsRegionalModule1Version, StringComparison.Ordinal))
            throw new FinalizationNotAuthorizedException(
                "The requested standards versions are not the ones this application template defines.");
        var regionalFiles = regionalBackboneWriterRegistry.Resolve(package.Application.Region).WriteRegionalBackbones(package);
        var index = ichIndexXmlWriter.Write(EctdBackboneComposition.AttachRegionalFiles(package, regionalFiles));

        var prefix = "payload/" + package.SequenceNumber + "/";
        var files = new List<ReleaseCaptureFile>();
        var assets = new List<ReleaseManifestAsset>();
        AddGenerated(files, prefix + index.FileName, index.XmlContent);
        foreach (var regional in regionalFiles) AddGenerated(files, prefix + regional.RelativePath, regional.Content);
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var published in package.PublishedFiles)
        {
            files.Add(ReleaseCaptureFile.Copy(prefix + published.Href, published.SourcePath, published.FileSize,
                published.Sha256, published.Md5));
            documents[package.SequenceNumber + "/" + published.Href] = published.Sha256;
        }
        foreach (var asset in profile.Assets)
        {
            var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                asset.LocalRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(source))
                throw new StandardsAssetMissingException($"Standards asset '{asset.Key}' was not found at '{source}'.");
            var (sha256, md5) = HashFile(source);
            if (!string.Equals(sha256, asset.Sha256, StringComparison.Ordinal))
                throw new StandardsAssetMissingException(
                    $"Standards asset '{asset.Key}' differs from its pinned digest; it cannot be captured.");
            var folder = string.Equals(asset.Category, "XSL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(source), ".xsl", StringComparison.OrdinalIgnoreCase)
                ? "util/style" : "util/dtd";
            var path = prefix + folder + "/" + Path.GetFileName(source);
            files.Add(ReleaseCaptureFile.Copy(path, source, new FileInfo(source).Length, sha256, md5));
            assets.Add(new ReleaseManifestAsset(asset.Key, asset.Category, asset.Version, path, sha256));
        }
        // FDA convention: index-md5.txt names only index.xml. It is generated from the same
        // bytes written above, so a backbone edit cannot leave a stale checksum list.
        var indexMd5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(index.XmlContent))).ToLowerInvariant();
        files.Add(ReleaseCaptureFile.Generated(prefix + "index-md5.txt",
            Encoding.UTF8.GetBytes($"{indexMd5}  index.xml{Environment.NewLine}")));

        // The comparison input digest is part of the run's input identity; the manifest
        // records it so the difference from the target's manifest is explained, not hidden.
        var comparison = storedRequest.ComparisonInputId is null ? null : run.Binding.InputManifestDigest;
        // Record the trimmed values that were compared, so a padded request produces the
        // same content identity as the identical trimmed one.
        var validation = new ReleaseValidationReference(run.ReportSha256!, run.Binding.ValidationMode.ToString(),
            run.Binding.ProfileSnapshotId, profile.IchEctdVersion, requestedRegional,
            run.Binding.RulesDigest, run.Binding.EngineVersion, comparison);
        // The backbone leaves are the delivered ICH + M1 stack; lifecycle targets name the
        // earlier leaf they modify, addressed exactly like the modified-file href.
        var leaves = package.IchBackboneLeaves.Concat(package.Module1Leaves)
            .GroupBy(leaf => leaf.PlacementId)
            .Select(group => group.First())
            .Select(leaf => new ReleaseManifestLeaf(leaf.PlacementId, leaf.DocumentId, leaf.LeafId, leaf.CtdSection,
                leaf.Operation, leaf.Href, leaf.NodeInstanceId, leaf.SortOrder)).ToArray();
        var lifecycle = package.IchBackboneLeaves.Concat(package.Module1Leaves)
            .Where(leaf => leaf.Lifecycle is not null)
            .Select(leaf => new ReleaseLifecycleEdge(leaf.LeafId, leaf.Operation, leaf.Lifecycle!.TargetSequenceNumber,
                leaf.Lifecycle.TargetBackboneRelativePath ?? "index.xml",
                leaf.Lifecycle.TargetLeafId ?? $"leaf-{leaf.Lifecycle.TargetPlacementId:N}",
                HistoryDigest(storedRequest.HistoryBaseline, leaf.Lifecycle.TargetSequenceNumber)))
            .ToArray();
        var nodes = (package.Nodes ?? []).Select(node => new ReleaseManifestNode(node.NodeInstanceId,
            node.ParentInstanceId, node.DefinitionKey, node.DefinitionVersion, node.Title, node.SortOrder,
            node.MetadataStatus.ToString(), node.Attributes)).ToArray();
        return new ReleaseCapturePreparation(new ReleaseCapturePlan(request.ApplicationId, package.SequenceNumber, files),
            assets, validation, storedRequest.HistoryBaseline.Clone(), documents, lifecycle, leaves, nodes);
    }

    // The archived baseline is the only source of exact historical content. A target the
    // baseline did not select stays null so the manifest admits the gap instead of
    // claiming an exactness it cannot prove.
    private static string? HistoryDigest(JsonElement baseline, string sequenceNumber)
    {
        if (baseline.ValueKind != JsonValueKind.Object ||
            !baseline.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return null;
        return entries.EnumerateArray()
            .Where(entry => string.Equals(entry.GetProperty("sequenceNumber").GetString(), sequenceNumber, StringComparison.Ordinal))
            .Select(entry => entry.GetProperty("contentDigest").GetString())
            .SingleOrDefault();
    }

    private static void AddGenerated(List<ReleaseCaptureFile> files, string path, string content) =>
        files.Add(ReleaseCaptureFile.Generated(path, Encoding.UTF8.GetBytes(content)));

    private static (string Sha256, string Md5) HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        // One read for both digests: a second pass could see a changed file.
        var buffer = new byte[65536];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            sha.AppendData(buffer, 0, read);
            md5.AppendData(buffer, 0, read);
        }
        return (Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant());
    }
}

// Reads the archived request exactly as the validation service wrote it.
internal static class StoredPackageValidationRequestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false) } };

    public static StoredPackageValidationRequest Read(PackageValidationRun run)
    {
        StoredPackageValidationRequest stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredPackageValidationRequest>(run.RequestJson, JsonOptions)
                ?? throw new InvalidDataException("The validation request is missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The archived validation request is malformed.", exception);
        }
        if (stored.SchemaVersion != 1) throw new InvalidDataException("Unsupported validation request schema.");
        // The archived baseline is the selection the run was enqueued with; its own digest
        // must be the binding digest, so a swapped baseline cannot accompany this run.
        var recorded = stored.HistoryBaseline.ValueKind == JsonValueKind.Object &&
            stored.HistoryBaseline.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String
            ? digest.GetString()
            : null;
        if (!string.Equals(recorded, run.Binding.HistoryBaselineDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The archived history baseline differs from the queued binding.");
        return stored;
    }
}
