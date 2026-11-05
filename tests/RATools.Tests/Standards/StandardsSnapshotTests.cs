using System.Text.Json;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Standards;

public sealed class StandardsSnapshotTests
{
    [Theory]
    [InlineData("util/dtd/same.dtd", "util/dtd/same.dtd")]
    [InlineData("util/dtd/SAME.dtd", "util/dtd/same.dtd")]
    public async Task ResourcePathsCannotHaveAmbiguousIdentities(string first, string second)
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider());
        var source = await service.CaptureAsync("us-fda-ectd-3.2.2");
        Assert.Throws<InvalidDataException>(() => StandardsProfileSnapshot.Create(source.Descriptor,
            [StandardsSnapshotResource.Create("a", "DTD", first, "application/xml-dtd", [1]), StandardsSnapshotResource.Create("b", "DTD", second, "application/xml-dtd", [2])], DateTime.UtcNow));
    }

    [Theory]
    [InlineData("util/CON.dtd")]
    [InlineData("util/trailing .dtd ")]
    [InlineData("util/control\u0001.dtd")]
    public void ResourceAddressesUsePortableCanonicalSegments(string path)
    {
        Assert.Throws<ArgumentException>(() => StandardsSnapshotResource.Create("key", "DTD", path, "application/xml-dtd", [1]));
    }

    [Fact]
    public async Task InvalidVerifiedProvenanceCannotBecomeAnActiveSnapshot()
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider());
        var source = await service.CaptureAsync("us-fda-ectd-3.2.2");
        var descriptor = source.Descriptor with { Provenance = [new("http://unverified.invalid", "", null, new DateOnly(2099, 1, 1), "Verified", "x")] };
        Assert.Throws<InvalidDataException>(() => StandardsProfileSnapshot.Create(descriptor, source.Resources, DateTime.UtcNow));
    }

    [Fact]
    public async Task VerifiedEvidenceMustNameCapturedResourceBytes()
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider());
        var source = await service.CaptureAsync("us-fda-ectd-3.2.2");
        var descriptor = source.Descriptor with { Evidence = [new("Rules", "Verified", "Claimed evidence with no resource", new string('f', 64))] };
        Assert.Throws<InvalidDataException>(() => StandardsProfileSnapshot.Create(descriptor, source.Resources, DateTime.UtcNow));
    }

    [Fact]
    public async Task TooManyProviderAssetsAreRejectedBeforeReadingMissingFiles()
    {
        var source = new FdaEctd322StandardsProfileProvider().GetProfile("us-fda-ectd-3.2.2");
        var many = source with { Assets = Enumerable.Range(0, 65).Select(index => source.Assets.First() with
        { Key = "asset-" + index, LocalRelativePath = "does-not-exist/asset-" + index + ".dtd" }).ToArray() };
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FixedProfile(many));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.CaptureAsync(source.TemplateKey));
        Assert.Contains("count", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegionalTemplateCannotBeCapturedWithAnotherRegionsRuleAlias()
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new EuEctd322StandardsProfileProvider());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CaptureAsync("eu-ectd-3.2.2", RATools.Application.PackageValidation.PackageValidationCatalog.UsProfile));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumptionRejectsSameBytesMovedAwayFromTheDeclaredProviderAndDeliveryPath(bool xmlLogicalPath)
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider());
        var snapshot = await service.CaptureAsync("us-fda-ectd-3.2.2");
        var changed = snapshot.Resources.ToDictionary(resource => resource.Key, StringComparer.Ordinal);
        if (!xmlLogicalPath)
        {
            var resource = changed["provider-ich-ectd-3-2-dtd"];
            changed[resource.Key] = StandardsSnapshotResource.Create(resource.Key, resource.Category, "reference/dtd/another.dtd", resource.MediaType, resource.ReadBytes());
        }
        else
        {
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(changed["xml-manifest"].ReadBytes())!;
            manifest["assets"]!.AsArray().Single(asset => asset!["id"]!.GetValue<string>() == "ich-dtd")!["logicalPath"] = "util/dtd/another.dtd";
            var original = changed["xml-manifest"];
            changed[original.Key] = StandardsSnapshotResource.Create(original.Key, original.Category, original.RelativePath, original.MediaType, JsonSerializer.SerializeToUtf8Bytes(manifest));
            var rules = System.Text.Json.Nodes.JsonNode.Parse(changed["rules"].ReadBytes())!;
            rules["sources"]!.AsArray().Single(source => source!["id"]!.GetValue<string>() == "ratools-package-xml-assets-v1")!["sha256"] = changed["xml-manifest"].Sha256;
            original = changed["rules"];
            changed[original.Key] = StandardsSnapshotResource.Create(original.Key, original.Category, original.RelativePath, original.MediaType, JsonSerializer.SerializeToUtf8Bytes(rules));
        }
        var ambiguous = StandardsProfileSnapshot.Create(snapshot.Descriptor, changed.Values, snapshot.AcquiredUtc);
        Assert.Throws<InvalidDataException>(() => RATools.Application.PackageValidation.PackageStandardsContext.FromSnapshot(ambiguous));
    }

    [Fact]
    public async Task CaptureStoresActualBytesAndRepeatedContentHasTheSameIdentity()
    {
        var store = new InMemoryStandardsSnapshotStore();
        var service = new StandardsSnapshotService(store, new FdaEctd322StandardsProfileProvider());
        var first = await service.CaptureAsync("us-fda-ectd-3.2.2");
        var again = await service.CaptureAsync("us-fda-ectd-3.2.2");
        Assert.Equal(first.Id, again.Id); Assert.StartsWith("sha256:", first.Id, StringComparison.Ordinal);
        Assert.Contains(first.Resources, resource => resource.Category == "NodeSchema");
        Assert.Contains(first.Resources, resource => resource.Category == "RuleMapping");
        Assert.Contains(first.Resources, resource => resource.Category == "ControlledVocabulary");
        var dtd = await service.OpenResourceAsync(first.Id, "xml-ich-dtd");
        Assert.True(dtd.Bytes.Length > 1000); Assert.Equal(dtd.Length, dtd.Bytes.Length);
        var bytes = dtd.Bytes; bytes[0] ^= 0xff;
        Assert.NotEqual(bytes[0], (await service.OpenResourceAsync(first.Id, "xml-ich-dtd")).Bytes[0]);
        Assert.Equal(first.Id, (await service.GetAsync(first.Id)).Id);
    }

    [Fact]
    public async Task BundledProfilesRemainTrialEvidenceAndCannotAuthorizeFormalSelection()
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new EuEctd322StandardsProfileProvider());
        var snapshot = await service.CaptureAsync("eu-ectd-3.2.2");
        Assert.Equal(RATools.Application.PackageValidation.PackageValidationCatalog.EuProfile, snapshot.RuleProfileAlias);
        var reasons = snapshot.Evaluate(null, new DateOnly(2026, 11, 5), snapshot.TemplateKey);
        Assert.Contains(reasons, reason => reason.StartsWith("ProfileNotActive:", StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.StartsWith("BusinessContextMissing:", StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.StartsWith("EvidenceIncomplete:", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyBindingSerializationAndRequestDigestStayExactlyCompatible()
    {
        var binding = new ValidationBinding(new string('a', 64), new string('b', 64), "legacy-profile", new string('c', 64),
            "engine-v1", PackageValidationMode.Development, new string('d', 64));
        Assert.DoesNotContain("StandardsSnapshotId", JsonSerializer.Serialize(binding), StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionDate", JsonSerializer.Serialize(binding), StringComparison.Ordinal);
        var exact = new ValidationBinding(new string('a', 64), new string('b', 64), "legacy-profile", new string('c', 64),
            "engine-v1", PackageValidationMode.Development, new string('d', 64), "sha256:" + new string('e', 64), new DateOnly(2026, 11, 5));
        Assert.NotEqual(binding, exact);
        var first = new PackageValidationRun(Guid.Parse("00000000-0000-0000-0000-000000000001"), "0000", binding,
            "{\"inputId\":\"external-input\"}", "legacy-key", new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("6927369047f12bd6bc62008790ab6bf2c31116b8e50a64a58442dfd4a017cae3", first.RequestDigest);
    }

    private sealed class FixedProfile(StandardsProfile profile) : IStandardsProfileProvider
    { public StandardsProfile GetProfile(string templateKey) => profile; }
}
