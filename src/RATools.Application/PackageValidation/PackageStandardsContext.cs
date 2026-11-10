using RATools.Application.Ctd;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Application.Standards;

namespace RATools.Application.PackageValidation;

// One immutable inspection context. History selects its own context by sequence;
// an absent legacy selection stays explicitly legacy, never rewritten as current.
public sealed class PackageStandardsContext(ValidationRuleCatalog catalog, PackageXmlAssets assets, SectionDefinitionSet nodeSchema,
    string? snapshotId = null, IReadOnlyDictionary<string, PackageStandardsContext>? history = null, DateOnly? selectionDate = null)
{
    public ValidationRuleCatalog Catalog { get; } = catalog;
    public PackageXmlAssets Assets { get; } = assets;
    public SectionDefinitionSet NodeSchema { get; } = nodeSchema;
    public string? SnapshotId { get; } = snapshotId;
    public DateOnly? SelectionDate { get; } = selectionDate;
    private Lazy<FdaM1AttributePolicy?> _fdaM1Policy = new(() => null);
    internal FdaM1AttributePolicy? FdaM1Policy => _fdaM1Policy.Value;
    private Lazy<FdaM1ConditionalPolicy?> _fdaM1ConditionalPolicy = new(() => null);
    internal FdaM1ConditionalPolicy? FdaM1ConditionalPolicy => _fdaM1ConditionalPolicy.Value;
    public PackageStandardsContext ForSequence(string sequence) => history?.GetValueOrDefault(sequence) ?? this;
    public static PackageStandardsContext Current => new(PackageValidationCatalog.Current, PackageXmlAssets.Current, IchSectionDefinitions.Current);
    public static PackageStandardsContext FromSnapshot(StandardsProfileSnapshot snapshot, IReadOnlyDictionary<string, PackageStandardsContext>? history = null, DateOnly? selectionDate = null)
    {
        snapshot.Verify();
        byte[] Resource(string key) => snapshot.Resources.SingleOrDefault(resource => resource.Key == key)?.ReadBytes() ?? throw new InvalidDataException("The standards snapshot is incomplete: " + key);
        using var rules = new MemoryStream(Resource("rules"));
        var catalog = PackageValidationCatalog.Load(rules);
        using var schema = new MemoryStream(Resource("node-schema"));
        var assets = PackageXmlAssets.FromSnapshot(snapshot, catalog);
        var profile = StandardsSnapshotService.Profile(snapshot);
        foreach (var declared in profile.Assets)
        {
            var mapped = assets.Assets.Values.SingleOrDefault(asset => asset.Path == declared.LocalRelativePath);
            var captured = snapshot.Resources.SingleOrDefault(resource => resource.Key == "provider-" + declared.Key);
            var delivery = (declared.Category.Equals("XSL", StringComparison.OrdinalIgnoreCase) ? "util/style/" : "util/dtd/") + Path.GetFileName(declared.LocalRelativePath);
            if (mapped is null || captured is null || captured.RelativePath != declared.LocalRelativePath || captured.Category != declared.Category ||
                mapped.LogicalPath != delivery || captured.Sha256 != declared.Sha256 || mapped.Sha256 != captured.Sha256)
                throw new InvalidDataException("The archived provider and XML inspection assets do not describe the same standard bytes; this snapshot remains readable but cannot authorize a new inspection or capture.");
        }
        return new(catalog, assets, IchSectionDefinitions.Load(schema), snapshot.Id, history, selectionDate)
        { _fdaM1Policy = new(() => FdaM1AttributePolicy.FromSnapshot(snapshot, catalog)),
            _fdaM1ConditionalPolicy = new(() => PackageValidation.FdaM1ConditionalPolicy.FromSnapshot(snapshot, catalog)) };
    }
}
