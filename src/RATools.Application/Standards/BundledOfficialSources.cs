using System.Globalization;
using System.Text.Json;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Application.Standards;

internal sealed record BundledOfficialSources(
    IReadOnlyList<StandardsSnapshotResource> Resources,
    IReadOnlyList<StandardsSnapshotProvenance> Provenance,
    StandardsSnapshotEvidence Evidence)
{
    private const string BindingDigest = "627ec5b1fd05231a7d2524b8b48194f582c0d881e78b07dc24190e5c40f52f03";
    private const string ResourcePrefix = "RATools.OfficialSources.";

    internal static readonly ValidationRuleSource IchQa = new("ich-ectd-qa", RuleSourceKind.RegulatorySpecification, "1.33",
        "https://admin.ich.org/sites/default/files/inline-files/eCTD_v3.2_Q%26A_%26_Specification_Change_Request_Document_v1.33.zip",
        "6c2fc32f3abea2d51c7a82e8d1f76b3228672dfd0968f1b9f912d43bb2454fdb", "Verified");
    private static readonly DateOnly IchQaAcquiredOn = new(2026, 11, 18);
    internal static readonly ValidationRuleSource IchDtdChecksum = new("ich-ectd-dtd-md5", RuleSourceKind.RegulatorySpecification, "3.2",
        "https://admin.ich.org/sites/default/files/inline-files/ich-ectd-3-2_md5_0.zip",
        "40f9a3f83d6ff5de7b4a46793ca2e1c4e9bab7df30b4ba2c79f8ffcf4b512224", "Verified");
    internal static readonly ValidationRuleSource StfSpecification = new("fda-stf-specification", RuleSourceKind.RegulatorySpecification, "2.6.1",
        "https://www.fda.gov/media/159383/download?attachment", "63132c3751682193f6f13db125ee03583b740680851c0f0e6412561438b482da", "Verified");
    internal static readonly ValidationRuleSource StfDtdChecksum = new("ich-stf-dtd-md5", RuleSourceKind.RegulatorySpecification, "2.2",
        "https://admin.ich.org/sites/default/files/inline-files/ich-stf-v2-2_md5.zip", "6f617b2f643cc9d8150d7dbfc73d0c2e02c3d080fe80ab8e3da7cbfe6aad32b5", "Verified");
    internal static readonly ValidationRuleSource StfStyleChecksum = new("ich-stf-style-md5", RuleSourceKind.RegulatorySpecification, "2.3",
        "https://admin.ich.org/sites/default/files/inline-files/ich-stf-stylesheet-2-3-md5.zip", "bddd62ebe1941cc698a608878eb32fc73cab17dd15f62f3bd6e43c8a821e56fa", "Verified");
    internal static readonly ValidationRuleSource StfPage = new("ich-stf-page", RuleSourceKind.RegulatorySpecification, "July 2024 / valid-values 6.0",
        "https://admin.ich.org/api/v1/nodes?loadEntities%5B%5D=paragraph&alias=%2Fpage%2Fstudy-tagging-file-specification-and-related-files",
        "e3f387d2833806f90158aa33aaa862a0efd72faf82f8543a1ad0696338423a51", "Verified");
    internal static readonly ValidationRuleSource StfValues = new("ich-stf-valid-values", RuleSourceKind.RegulatorySpecification, "6.0",
        "https://admin.ich.org/sites/default/files/inline-files/eCTD_v.3.2_Valid_Values_v6.0.zip",
        "dd750e6c9f17aaf2797c553150c67ffeb483f5491fb2ed7df76e26b933037b80", "Verified");
    // Source acquisition was 2026-11-20 Asia/Shanghai, still 2026-11-19 UTC.
    private static readonly DateOnly StfSourcesAcquiredOn = new(2026, 11, 19);
    internal static readonly ValidationRuleSource StudyDataTechnicalGuide = new("fda-study-data-technical-guide", RuleSourceKind.RegulatorySpecification, "October 2026",
        "https://www.fda.gov/media/153632/download", "3455b7dc93d1866bc94684d8dd3200f15a297e8d84d36e4be63d45e36a1bf689", "Verified");
    // Reacquired from the official endpoint and matched to the existing archive on this date.
    private static readonly DateOnly StudyDataGuideAcquiredOn = new(2026, 11, 20);

    public static BundledOfficialSources? Capture(StandardsProfile profile, DateOnly captureDate)
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
        if (captureDate >= IchQaAcquiredOn && selected.Any(item => item.GetProperty("id").GetString() == "fda-criteria"))
        {
            var qa = ReadResource("official-ich-ectd-qa", "OfficialSource", "official-sources/ich-ectd-qa-v1.33.zip",
                "application/zip", IchQa.Id, IchQa.Sha256!);
            resources.Add(qa);
            provenance.Add(new(IchQa.Url, IchQa.Version, IchQaAcquiredOn, IchQaAcquiredOn, IchQa.VerificationStatus, qa.Sha256));
            var checksum = ReadResource("official-ich-ectd-dtd-md5", "OfficialSource", "official-sources/ich-ectd-main-dtd-md5.zip",
                "application/zip", IchDtdChecksum.Id, IchDtdChecksum.Sha256!);
            resources.Add(checksum);
            provenance.Add(new(IchDtdChecksum.Url, IchDtdChecksum.Version, IchQaAcquiredOn, IchQaAcquiredOn, IchDtdChecksum.VerificationStatus, checksum.Sha256));
        }
        if (captureDate >= StfSourcesAcquiredOn && selected.Any(item => item.GetProperty("id").GetString() == "fda-criteria"))
            foreach (var (source, path, media) in new[]
            {
                (StfSpecification, "fda-stf-specification.pdf", "application/pdf"),
                (StfDtdChecksum, "ich-stf-v2-2-md5.zip", "application/zip"),
                (StfStyleChecksum, "ich-stf-stylesheet-2-3-md5.zip", "application/zip"),
                (StfPage, "ich-stf-page.json", "application/json"),
                (StfValues, "valid-values-v6.zip", "application/zip")
            })
            {
                var resource = ReadResource("official-" + source.Id, "OfficialSource", "official-sources/" + path, media, source.Id, source.Sha256!);
                resources.Add(resource);
                provenance.Add(new(source.Url, source.Version, StfSourcesAcquiredOn, StfSourcesAcquiredOn, source.VerificationStatus, resource.Sha256));
            }
        if (captureDate >= StudyDataGuideAcquiredOn && selected.Any(item => item.GetProperty("id").GetString() == "fda-criteria"))
        {
            var source = StudyDataTechnicalGuide;
            var resource = ReadResource("official-" + source.Id, "OfficialSource", "official-sources/fda-study-data-technical-conformance-guide.pdf",
                "application/pdf", source.Id, source.Sha256!);
            resources.Add(resource);
            provenance.Add(new(source.Url, source.Version, StudyDataGuideAcquiredOn, StudyDataGuideAcquiredOn, source.VerificationStatus, resource.Sha256));
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
