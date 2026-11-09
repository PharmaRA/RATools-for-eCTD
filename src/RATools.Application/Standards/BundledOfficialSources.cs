using System.Globalization;
using System.Text.Json;
using RATools.Domain.Standards;

namespace RATools.Application.Standards;

internal sealed record BundledOfficialSources(
    IReadOnlyList<StandardsSnapshotResource> Resources,
    IReadOnlyList<StandardsSnapshotProvenance> Provenance,
    StandardsSnapshotEvidence Evidence)
{
    private const string BindingDigest = "627ec5b1fd05231a7d2524b8b48194f582c0d881e78b07dc24190e5c40f52f03";
    private const string ResourcePrefix = "RATools.OfficialSources.";

    public static BundledOfficialSources? Capture(StandardsProfile profile)
    {
        var binding = ReadResource("official-source-bindings", "Sources", "metadata/official-sources/bindings-2026-11-09.json",
            "application/json", "Bindings", BindingDigest);
        using var document = JsonDocument.Parse(binding.ReadBytes());
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported official source binding schema.");
        var selection = root.GetProperty("profiles").EnumerateArray().SingleOrDefault(candidate =>
            candidate.GetProperty("templateKey").GetString() == profile.TemplateKey &&
            candidate.GetProperty("region").GetString() == profile.Region &&
            candidate.GetProperty("ichVersion").GetString() == profile.IchEctdVersion &&
            candidate.GetProperty("regionalVersion").GetString() == profile.RegionalModule1Version &&
            candidate.GetProperty("guideVersion").GetString() == profile.TechnicalConformanceGuideVersion &&
            candidate.GetProperty("criteriaVersion").GetString() == profile.ValidationCriteriaVersion);
        if (selection.ValueKind == JsonValueKind.Undefined) return null;

        var inventory = root.GetProperty("sources").EnumerateArray().ToDictionary(source => source.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var selected = selection.GetProperty("sourceIds").EnumerateArray().Select(id => inventory[id.GetString()!]).ToArray();
        if (profile.OfficialReferences.Any(url => !selected.Any(source => source.GetProperty("sourceUrl").GetString() == url))) return null;
        var resources = new List<StandardsSnapshotResource>
        {
            binding,
            ReadResource("official-source-inventory", "SourceInventory", "metadata/official-sources/inventory-2026-11-09.json",
                "application/json", "Inventory", root.GetProperty("inventory").GetProperty("sha256").GetString()!)
        };
        var provenance = new List<StandardsSnapshotProvenance>();
        foreach (var source in selected)
        {
            var id = source.GetProperty("id").GetString()!;
            var resource = ReadResource("official-" + id, "OfficialSource", source.GetProperty("snapshotPath").GetString()!,
                source.GetProperty("mediaType").GetString()!, id, source.GetProperty("sha256").GetString()!);
            if (resource.Length != source.GetProperty("bytes").GetInt64())
                throw new InvalidDataException("Official source length differs from its acquisition record.");
            resources.Add(resource);
            provenance.Add(new(source.GetProperty("sourceUrl").GetString()!, source.GetProperty("version").GetString()!,
                DateOnly.FromDateTime(DateTimeOffset.Parse(source.GetProperty("retrievedUtc").GetString()!, CultureInfo.InvariantCulture).UtcDateTime),
                DateOnly.ParseExact(source.GetProperty("verifiedOn").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                "Verified", resource.Sha256));
        }
        return new(resources, provenance.OrderBy(source => source.SourceUrl, StringComparer.Ordinal).ToArray(),
            new("Sources", "Verified", "Official originals, versions, acquisition dates and applicability are bound to captured source bytes. Rule implementation and external comparison remain separately unqualified.", binding.Sha256));
    }

    private static StandardsSnapshotResource ReadResource(string key, string category, string path, string mediaType, string embedded, string expectedDigest)
    {
        using var stream = typeof(BundledOfficialSources).Assembly.GetManifestResourceStream(ResourcePrefix + embedded)
            ?? throw new InvalidDataException("A pinned official source resource is missing.");
        if (stream.Length is <= 0 or > 8 * 1024 * 1024)
            throw new InvalidDataException("Official source resource byte limit exceeded.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var resource = StandardsSnapshotResource.Create(key, category, path, mediaType, bytes.ToArray());
        if (resource.Sha256 != expectedDigest)
            throw new InvalidDataException("Official source bytes differ from the pinned acquisition record.");
        return resource;
    }
}
