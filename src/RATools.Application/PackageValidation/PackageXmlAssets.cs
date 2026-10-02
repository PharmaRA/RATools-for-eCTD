using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RATools.Application.PackageValidation;

public enum BackboneKind { Ich, UsRegional, EuRegional }

public sealed record PackageXmlBackboneProfile(BackboneKind Kind, string DocumentTypeName, string RootLocalName,
    string NamespaceUri, string DtdVersion, string MainAssetId, IReadOnlyList<string> AllowedAssetIds,
    string DefaultRelativePath, string RuleId, IReadOnlyList<string> StylesheetAssetIds);

public sealed record PackageXmlAsset(string Id, string Path, string LogicalPath, string Sha256);

public sealed class PackageXmlAssets
{
    private static readonly Lazy<PackageXmlAssets> Snapshot = new(Load);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly FrozenDictionary<string, byte[]> _bytes;
    private readonly FrozenDictionary<string, IReadOnlyList<PackageXmlBackboneProfile>> _profiles;

    private PackageXmlAssets(string digest, AssetFile file, Dictionary<string, byte[]> bytes)
    {
        Digest = digest;
        Assets = file.Assets.ToFrozenDictionary(asset => asset.Id, StringComparer.Ordinal);
        _bytes = bytes.ToFrozenDictionary(StringComparer.Ordinal);
        _profiles = file.Profiles.ToFrozenDictionary(profile => profile.ProfileSnapshotId,
            profile => (IReadOnlyList<PackageXmlBackboneProfile>)Array.AsReadOnly(profile.Backbones.Select(backbone =>
                new PackageXmlBackboneProfile(backbone.Role, backbone.DocumentTypeName, backbone.RootLocalName, backbone.NamespaceUri,
                    backbone.DtdVersion, backbone.MainAssetId, Array.AsReadOnly(backbone.AllowedAssetIds), backbone.DefaultRelativePath, backbone.RuleId,
                    Array.AsReadOnly(backbone.StylesheetAssetIds))).ToArray()), StringComparer.Ordinal);
    }

    public static PackageXmlAssets Current => Snapshot.Value;
    public string Digest { get; }
    public IReadOnlyDictionary<string, PackageXmlAsset> Assets { get; }

    public IReadOnlyList<PackageXmlBackboneProfile> ForProfile(string profileSnapshotId) => _profiles.TryGetValue(profileSnapshotId, out var profile)
        ? profile : throw new ArgumentException("Select a supported explicit XML profile snapshot.", nameof(profileSnapshotId));

    internal Stream Open(string assetId) => new MemoryStream(_bytes[assetId], writable: false);

    private static PackageXmlAssets Load()
    {
        var manifest = ReadResource("RATools.PackageXmlAssetManifest");
        var digest = Hash(manifest);
        var expected = PackageValidationCatalog.Current.Sources["ratools-package-xml-assets-v1"].Sha256;
        if (digest != expected) throw new InvalidOperationException("The XML asset manifest differs from the catalog-bound source digest.");
        var file = JsonSerializer.Deserialize<AssetFile>(manifest, JsonOptions) ?? throw new InvalidOperationException("XML asset manifest is empty.");
        if (file.SchemaVersion != 1 || file.Version != "package-xml-assets-v1" ||
            file.NodeSchemaPath != "reference/publisher/ich-3.2.2-nodes.json" || Hash(ReadResource("RATools.IchNodeSchema")) != file.NodeSchemaSha256)
            throw new InvalidOperationException("Unsupported or changed XML asset/schema snapshot.");
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var asset in file.Assets)
        {
            PackageLogicalPath.Validate(asset.LogicalPath);
            var content = ReadResource("RATools.PackageXml." + asset.Id);
            if (Hash(content) != asset.Sha256) throw new InvalidOperationException($"Pinned XML asset '{asset.Id}' has changed.");
            bytes.Add(asset.Id, content);
        }
        foreach (var profile in file.Profiles)
        {
            _ = PackageValidationCatalog.Current.ForProfile(profile.ProfileSnapshotId);
            if (profile.Backbones.Count(backbone => backbone.Role == BackboneKind.Ich) != 1)
                throw new InvalidOperationException("Every XML profile requires exactly one ICH backbone definition.");
            foreach (var backbone in profile.Backbones)
                if (!backbone.AllowedAssetIds.Contains(backbone.MainAssetId, StringComparer.Ordinal) || backbone.AllowedAssetIds.Any(id => !bytes.ContainsKey(id)) ||
                    backbone.StylesheetAssetIds.Any(id => !bytes.ContainsKey(id)))
                    throw new InvalidOperationException("XML profile contains an unresolved asset reference.");
        }
        return new(digest, file, bytes);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(PackageXmlAssets).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Pinned resource '{name}' is missing.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private sealed record AssetFile(int SchemaVersion, string Version, string ResolutionPolicy, PackageXmlAsset[] Assets,
        ProfileFile[] Profiles, string NodeSchemaPath, string NodeSchemaSha256);
    private sealed record ProfileFile(string ProfileSnapshotId, BackboneFile[] Backbones);
    private sealed record BackboneFile(BackboneKind Role, string DocumentTypeName, string RootLocalName, string NamespaceUri,
        string DtdVersion, string MainAssetId, string[] AllowedAssetIds, string DefaultRelativePath, string RuleId, string[] StylesheetAssetIds);
}
