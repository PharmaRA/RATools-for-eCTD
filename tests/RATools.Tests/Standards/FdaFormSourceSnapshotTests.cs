using System.Security.Cryptography;
using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Standards;

public sealed class FdaFormSourceSnapshotTests
{
    private const string InventoryKey = "official-fda-form-inventory";
    private const string InventoryHash = "d6784bfd301bd384865e81539857e2d5a4efdf837ac6a27903f956b236796e32";
    private const string FormHash = "921983dcb9ac14308617684c52e516f283cdf343adb7e0f3ab52fef09b1b1e7e";
    private static readonly string[] Forms = ["1571", "2252", "2253", "356h", "3674", "3938", "3988"];

    [Fact]
    public async Task CurrentCaptureFreezesAllSevenOriginalsWithExactProvenanceAndBudgets()
    {
        var snapshot = await Service().CaptureAsync("us-fda-ectd-3.2.2");
        var inventory = Assert.Single(snapshot.Resources, item => item.Key == InventoryKey);
        Assert.Equal("SourceInventory", inventory.Category);
        Assert.Equal(InventoryHash, Sha(inventory.ReadBytes()));
        using var json = JsonDocument.Parse(inventory.ReadBytes());
        var sources = json.RootElement.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(Forms, sources.Select(item => item.GetProperty("id").GetString()![9..]).Order(StringComparer.Ordinal));
        var context = PackageStandardsContext.FromSnapshot(snapshot);
        foreach (var source in sources)
        {
            var id = source.GetProperty("id").GetString()!;
            var resource = Assert.Single(snapshot.Resources, item => item.Key == "official-" + id);
            Assert.Equal("OfficialSource", resource.Category);
            Assert.Equal("application/pdf", resource.MediaType);
            Assert.Equal(source.GetProperty("bytes").GetInt64(), resource.Length);
            Assert.Equal(source.GetProperty("sha256").GetString(), resource.Sha256);
            Assert.Equal(resource.Sha256, Sha(resource.ReadBytes()));
            var provenance = Assert.Single(snapshot.Descriptor.Provenance, item => item.Sha256 == resource.Sha256);
            Assert.Equal(source.GetProperty("requestedUrl").GetString(), provenance.SourceUrl);
            Assert.Equal("Archived PDF modification " + source.GetProperty("pdfModificationDate").GetString(), provenance.Version);
            Assert.Equal(new DateOnly(2026, 11, 20), provenance.AcquiredOn);
            Assert.Equal(new DateOnly(2026, 11, 26), provenance.VerifiedOn);
            Assert.Equal("Verified", provenance.VerificationStatus);
            Assert.True(context.HasArchivedOfficialSource(new(id, RuleSourceKind.RegulatorySpecification,
                provenance.Version, provenance.SourceUrl, resource.Sha256, "Verified")));
        }
        Assert.True(snapshot.Resources.Count <= 64);
        Assert.True(snapshot.Resources.Sum(item => item.Length) <= 32L * 1024 * 1024);
        Assert.All(snapshot.Resources, item => Assert.True(item.Length <= 8L * 1024 * 1024));
        Assert.DoesNotContain(snapshot.Resources, item => item.Key is "official-fda-form-3397" or "official-fda-form-3792" or "official-fda-form-3794" or "official-fda-form-2567");
        Assert.Contains(snapshot.Descriptor.Evidence, item => item.Category == "Rules" && item.Status == "Unverified");
        Assert.All(snapshot.Descriptor.Scopes, scope => Assert.False(scope.Finalize));
    }

    [Theory]
    [InlineData("us-fda-ectd-3.2.2", 25, false)]
    [InlineData("us-fda-ectd-3.2.2", 26, true)]
    [InlineData("eu-ectd-3.2.2", 26, false)]
    public async Task SourcesAreNotBorrowedAcrossQualificationDateOrRegion(string template, int day, bool expected)
    {
        var snapshot = await Service(day: day).CaptureAsync(template);
        Assert.Equal(expected, snapshot.Resources.Any(item => item.Key == InventoryKey));
        Assert.Equal(expected, snapshot.Descriptor.Provenance.Any(item => item.Sha256 == FormHash));
        Assert.Equal(expected ? 7 : 0, snapshot.Resources.Count(item => Forms.Any(form => item.Key == "official-fda-form-" + form)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvenanceRejectsMissingOrReplacedFormBytes(bool replace)
    {
        var snapshot = await Service().CaptureAsync("us-fda-ectd-3.2.2");
        var original = Assert.Single(snapshot.Resources, item => item.Key == "official-fda-form-2252");
        var resources = snapshot.Resources.Where(item => item.Key != original.Key).ToList();
        if (replace) resources.Add(StandardsSnapshotResource.Create(original.Key, original.Category, original.RelativePath, original.MediaType, [1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc));
    }

    [Fact]
    public async Task LegacySnapshotDoesNotBorrowCurrentFormOriginals()
    {
        var snapshot = await Service().CaptureAsync("us-fda-ectd-3.2.2");
        var provenance = Assert.Single(snapshot.Descriptor.Provenance, item => item.Sha256 == FormHash);
        var legacy = StandardsProfileSnapshot.Create(snapshot.Descriptor with
        { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != FormHash).ToArray() },
            snapshot.Resources.Where(item => item.Sha256 != FormHash), snapshot.AcquiredUtc);
        Assert.False(PackageStandardsContext.FromSnapshot(legacy).HasArchivedOfficialSource(new("fda-form-2252", RuleSourceKind.RegulatorySpecification,
            provenance.Version, provenance.SourceUrl, FormHash, "Verified")));
    }

    [Fact]
    public async Task ReopenedSnapshotReadsOriginalsWithoutCurrentProvider()
    {
        var store = new InMemoryStandardsSnapshotStore();
        var snapshot = await Service(store).CaptureAsync("us-fda-ectd-3.2.2");
        var reopened = new StandardsSnapshotService(store, new UnavailableProvider());
        var original = await reopened.OpenResourceAsync(snapshot.Id, "official-fda-form-2252");
        Assert.Equal(FormHash, Sha(original.Bytes));
        original.Bytes[0] ^= 0xff;
        Assert.Equal(FormHash, Sha((await reopened.OpenResourceAsync(snapshot.Id, original.Key)).Bytes));
        Assert.Equal(InventoryHash, Sha((await reopened.OpenResourceAsync(snapshot.Id, InventoryKey)).Bytes));
    }

    [Theory]
    [InlineData("criteria")]
    [InlineData("region")]
    public async Task OtherProfileVersionsCannotBorrowFormSources(string field)
    {
        var profile = new FdaEctd322StandardsProfileProvider().GetProfile("us-fda-ectd-3.2.2");
        profile = field == "criteria" ? profile with { ValidationCriteriaVersion = "unknown" } : profile with { Region = "EU" };
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FixedProvider(profile), clock: new Clock(26)).CaptureAsync(profile.TemplateKey);
        Assert.DoesNotContain(snapshot.Resources, item => item.Key == InventoryKey);
        Assert.DoesNotContain(snapshot.Descriptor.Provenance, item => item.Sha256 == FormHash);
    }

    private sealed class FixedProvider(StandardsProfile profile) : IStandardsProfileProvider
    {
        public StandardsProfile GetProfile(string templateKey) => profile;
    }
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static StandardsSnapshotService Service(InMemoryStandardsSnapshotStore? store = null, int day = 26) =>
        new(store ?? new(), new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]), clock: new Clock(day));
    private sealed class Clock(int day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, day, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class UnavailableProvider : IStandardsProfileProvider
    {
        public StandardsProfile GetProfile(string templateKey) => throw new InvalidOperationException("Archived forms must not consult the current provider.");
    }
}
