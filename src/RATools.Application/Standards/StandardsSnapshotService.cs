using RATools.Application.PackageValidation;
using RATools.Application.Applications.EctdTemplates;
using RATools.Domain.Standards;
using System.Text.Json;

namespace RATools.Application.Standards;

public interface IStandardsSnapshotStore
{
    Task<StandardsProfileSnapshot> AddAsync(StandardsProfileSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<StandardsProfileSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default);
}
public sealed class StandardsSnapshotOptions { public string AssetRootPath { get; set; } = AppContext.BaseDirectory; }
public sealed record StandardsSnapshotContent(string Key, string MediaType, long Length, string Sha256, byte[] Bytes);
public sealed record StandardsSnapshotResourceView(string Key, string Category, string RelativePath, long Length, string Sha256, string MediaType);
public sealed record StandardsSnapshotView(string Id, int SchemaVersion, string TemplateKey, string Region, string IchEctdVersion, string? RegionalModule1Version,
    string GuideVersion, string CriteriaVersion, string RuleProfileAlias, StandardsSnapshotLifecycle Lifecycle,
    IReadOnlyList<StandardsSnapshotProvenance> Provenance, IReadOnlyList<StandardsBusinessScope> Scopes, IReadOnlyList<StandardsSnapshotEvidence> Evidence,
    IReadOnlyList<StandardsSnapshotResourceView> Resources, DateTime AcquiredUtc);
public sealed class StandardsSelectionException(IReadOnlyList<string> reasons) : InvalidOperationException(string.Join(" | ", reasons))
{ public IReadOnlyList<string> Reasons { get; } = reasons; }

public sealed class StandardsSnapshotService(IStandardsSnapshotStore store, IStandardsProfileProvider profiles,
    StandardsSnapshotOptions? options = null, TimeProvider? clock = null)
{
    public async Task<StandardsProfileSnapshot> CaptureAsync(string templateKey, string? ruleProfileAlias = null, CancellationToken cancellationToken = default)
    {
        var profile = profiles.GetProfile(templateKey);
        if (profile.Assets.Count > 64 || profile.OfficialReferences.Count > 64 ||
            new[] { profile.TemplateKey, profile.DisplayName, profile.RegulatoryAgency, profile.Region, profile.IchEctdVersion, profile.RegionalModule1Version,
                profile.TechnicalConformanceGuideVersion, profile.ValidationCriteriaVersion }.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512) ||
            profile.OfficialReferences.Any(url => url.Length > 4096) || profile.Assets.Any(asset => asset.LocalRelativePath.Length > 230 || asset.SourceUrl.Length > 4096 || asset.Key.Length > 128))
            throw new InvalidDataException("Snapshot metadata or resource count exceeds its bounds.");
        var alias = ruleProfileAlias ?? (profile.TemplateKey == EctdTemplateRegistry.EuTemplateKey ? PackageValidationCatalog.EuProfile : PackageValidationCatalog.UsProfile);
        _ = PackageValidationCatalog.Current.ForProfile(alias);
        if (PackageValidationCatalog.Current.Profiles[alias].TemplateKey != profile.TemplateKey && alias != PackageValidationCatalog.IchProfile)
            throw new ArgumentException("The selected rule profile belongs to a different template.", nameof(ruleProfileAlias));
        var resources = new List<StandardsSnapshotResource>();
        long remaining = 32 * 1024 * 1024;
        foreach (var resource in EmbeddedStandardsResources())
        {
            if (resources.Count >= 64 || remaining <= 0) throw new InvalidDataException("Snapshot resource budget exceeded.");
            using var embedded = OpenEmbedded(resource.Embedded);
            var bytes = ReadBounded(embedded, Math.Min(remaining, 8 * 1024 * 1024)); remaining -= bytes.Length;
            resources.Add(StandardsSnapshotResource.Create(resource.Key, resource.Category, resource.Path, resource.MediaType, bytes));
        }
        var profileBytes = JsonSerializer.SerializeToUtf8Bytes(profile);
        if (resources.Count >= 64 || profileBytes.Length > remaining) throw new InvalidDataException("Snapshot resource budget exceeded."); remaining -= profileBytes.Length;
        resources.Add(StandardsSnapshotResource.Create("profile", "Profile", "metadata/profile.json", "application/json", profileBytes));
        if (profile.Assets.Count > 64 - resources.Count) throw new InvalidDataException("Snapshot resource count exceeded.");
        foreach (var asset in profile.Assets)
        {
            PackageLogicalPath.Validate(asset.LocalRelativePath, 230);
            var root = Path.GetFullPath((options ?? new()).AssetRootPath); var source = Path.GetFullPath(Path.Combine(root, asset.LocalRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!source.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("The standards asset path escaped its root.");
            var directory = source;
            while (directory is not null) { if ((File.Exists(directory) || Directory.Exists(directory)) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Standards assets must not traverse links."); directory = Path.GetDirectoryName(directory); }
            var info = new FileInfo(source); if (!info.Exists || info.Length > 8 * 1024 * 1024) throw new InvalidDataException("Standards asset is missing or exceeds its byte limit.");
            using var stream = File.OpenRead(source); var bytes = ReadBounded(stream, Math.Min(remaining, 8 * 1024 * 1024)); remaining -= bytes.Length;
            var resource = StandardsSnapshotResource.Create("provider-" + asset.Key, asset.Category, asset.LocalRelativePath, Media(asset.LocalRelativePath), bytes);
            if (resource.Sha256 != asset.Sha256) throw new InvalidDataException("The provider asset changed during capture."); resources.Add(resource);
        }
        var lifecycle = profile.Lifecycle;
        var descriptor = new StandardsSnapshotDescriptor(profile.TemplateKey, profile.DisplayName, profile.RegulatoryAgency, profile.Region,
            profile.IchEctdVersion, profile.RegionalModule1Version, profile.TechnicalConformanceGuideVersion, profile.ValidationCriteriaVersion, alias,
            new(lifecycle?.EffectiveFrom ?? profile.Assets.Select(asset => asset.SupportedFrom ?? DateOnly.MinValue).DefaultIfEmpty(DateOnly.MinValue).Max(), lifecycle?.RetiredFrom,
                lifecycle is null ? StandardsSnapshotStatus.AcquiredNotActive : Enum.Parse<StandardsSnapshotStatus>(lifecycle.Status.ToString()),
                lifecycle?.RetirementPolicy ?? "Retain historical bytes; formal activation requires verified regional scope and evidence."),
            profile.OfficialReferences.Order(StringComparer.Ordinal).Select(url => new StandardsSnapshotProvenance(url, url.Contains("ich.org", StringComparison.OrdinalIgnoreCase) ? profile.IchEctdVersion : "Unverified-version-selection",
                null, null, "Unverified", null)).ToArray(),
            [new("*", "*", "*", "*", "*", "*", true, true, false, "TrialOnly")],
            [new("Sources", "Unverified", "Bundled assets are hash pinned; current official applicability has not been independently verified."),
             new("ControlledVocabulary", "Missing", "Captured DTD/schema constraints are retained; complete regional business code lists remain unqualified."),
             new("MetadataMapping", "Unverified", "P6 regional metadata mapping qualification remains pending."),
             new("Writer", "Unverified", "Regional writer scope requires the P6 evidence matrix."),
             new("Rules", "Unverified", "Pinned rule mappings do not establish regional qualification."),
             new("ExternalComparison", "Missing", "No independent regional comparison evidence activates this snapshot.")]);
        return await store.AddAsync(StandardsProfileSnapshot.Create(descriptor, resources, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime), cancellationToken).ConfigureAwait(false);
    }
    public async Task<StandardsProfileSnapshot> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var snapshot = await store.GetAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The standards snapshot was not found."); snapshot.Verify(); return snapshot;
    }
    public async Task<StandardsSnapshotContent> OpenResourceAsync(string id, string key, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        var resource = snapshot.Resources.SingleOrDefault(resource => resource.Key == key) ?? throw new KeyNotFoundException("The snapshot resource was not found.");
        return new(resource.Key, resource.MediaType, resource.Length, resource.Sha256, resource.ReadBytes());
    }
    public static StandardsProfile Profile(StandardsProfileSnapshot snapshot)
    {
        snapshot.Verify(); var profile = JsonSerializer.Deserialize<StandardsProfile>(snapshot.Resources.SingleOrDefault(resource => resource.Key == "profile")?.ReadBytes() ?? throw new InvalidDataException("The archived standards descriptor resource is missing."))
            ?? throw new InvalidDataException("The archived standards descriptor is missing.");
        if (profile.TemplateKey != snapshot.TemplateKey || profile.IchEctdVersion != snapshot.Descriptor.IchEctdVersion || profile.RegionalModule1Version != snapshot.Descriptor.RegionalModule1Version)
            throw new InvalidDataException("Archived profile versions disagree with the snapshot identity.");
        return profile;
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The public service exposes one consistent instance API for captured and stored snapshots.")]
    public StandardsSnapshotView View(StandardsProfileSnapshot snapshot)
    {
        snapshot.Verify(); var descriptor = snapshot.Descriptor;
        return new(snapshot.Id, snapshot.SchemaVersion, descriptor.TemplateKey, descriptor.Region, descriptor.IchEctdVersion, descriptor.RegionalModule1Version,
            descriptor.GuideVersion, descriptor.CriteriaVersion, descriptor.RuleProfileAlias, descriptor.Lifecycle, descriptor.Provenance, descriptor.Scopes, descriptor.Evidence,
            snapshot.Resources.Select(resource => new StandardsSnapshotResourceView(resource.Key, resource.Category, resource.RelativePath, resource.Length, resource.Sha256, resource.MediaType)).ToArray(), snapshot.AcquiredUtc);
    }
    private static IEnumerable<Resource> EmbeddedStandardsResources()
    {
        foreach (var resource in new[] { new Resource("rules", "RuleMapping", "reference/publisher/package-rules-v1.json", "RATools.PackageValidationCatalog", "application/json"),
            new Resource("xml-manifest", "XmlAssetManifest", "reference/publisher/package-xml-assets-v1.json", "RATools.PackageXmlAssetManifest", "application/json"),
            new Resource("node-schema", "NodeSchema", "reference/publisher/ich-3.2.2-nodes.json", "RATools.IchNodeSchema", "application/json"),
            new Resource("node-paths", "NodeSchema", "reference/publisher/ich-3.2.2-node-paths.json", "RATools.IchNodePaths", "application/json"),
            new Resource("controlled-vocabulary-source", "ControlledVocabulary", "reference/publisher/vocabulary-node-schema.json", "RATools.IchNodeSchema", "application/json") })
            yield return resource;
        foreach (var asset in PackageXmlAssets.Current.Assets.Values.OrderBy(asset => asset.Id, StringComparer.Ordinal))
            yield return new("xml-" + asset.Id, Path.GetExtension(asset.LogicalPath) == ".xsl" ? "XSL" : "DTD", asset.LogicalPath, "RATools.PackageXml." + asset.Id, Media(asset.LogicalPath));
    }
    private static Stream OpenEmbedded(string name) => typeof(StandardsSnapshotService).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("A pinned standards resource is missing.");
    private static byte[] ReadBounded(Stream stream, long maximum)
    { using var output = new MemoryStream(); var buffer = new byte[65536]; int count; while ((count = stream.Read(buffer)) > 0) { if (output.Length + count > maximum) throw new InvalidDataException("Snapshot resource byte budget exceeded."); output.Write(buffer, 0, count); } return output.ToArray(); }
    private static string Media(string path) => Path.GetExtension(path) switch { ".json" => "application/json", ".xsl" => "application/xml", _ => "application/xml-dtd" };
    private sealed record Resource(string Key, string Category, string Path, string Embedded, string MediaType);
}
