using System.Security.Cryptography;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Standards;

public sealed class StudyDataSourceSnapshotTests
{
    private const string Key = "official-fda-study-data-technical-guide";
    private const string Digest = "3455b7dc93d1866bc94684d8dd3200f15a297e8d84d36e4be63d45e36a1bf689";
    private static readonly ValidationRuleSource Source = new("fda-study-data-technical-guide", RuleSourceKind.RegulatorySpecification,
        "October 2026", "https://www.fda.gov/media/153632/download", Digest, "Verified");

    [Fact]
    public async Task CurrentCaptureFreezesExactGuideBytesAndProvenance()
    {
        var snapshot = await Service().CaptureAsync("us-fda-ectd-3.2.2");
        var resource = Assert.Single(snapshot.Resources, item => item.Key == Key);
        Assert.Equal("OfficialSource", resource.Category);
        Assert.Equal("application/pdf", resource.MediaType);
        Assert.Equal(1399163, resource.Length);
        Assert.Equal(Digest, resource.Sha256);
        Assert.Equal(Digest, Convert.ToHexString(SHA256.HashData(resource.ReadBytes())).ToLowerInvariant());
        var provenance = Assert.Single(snapshot.Descriptor.Provenance, item => item.Sha256 == Digest);
        Assert.Equal(Source.Url, provenance.SourceUrl);
        Assert.Equal(Source.Version, provenance.Version);
        Assert.Equal("Verified", provenance.VerificationStatus);
        Assert.Equal(new DateOnly(2026, 11, 20), provenance.AcquiredOn);
        Assert.Equal(new DateOnly(2026, 11, 20), provenance.VerifiedOn);
        Assert.True(PackageStandardsContext.FromSnapshot(snapshot).HasArchivedOfficialSource(Source));
    }

    [Theory]
    [InlineData("us-fda-ectd-3.2.2", 19, false)]
    [InlineData("us-fda-ectd-3.2.2", 20, true)]
    [InlineData("eu-ectd-3.2.2", 20, false)]
    public async Task SourceIsNotBorrowedAcrossDateOrRegion(string template, int day, bool expected)
    {
        var snapshot = await Service(day: day).CaptureAsync(template);
        Assert.Equal(expected, snapshot.Resources.Any(item => item.Key == Key));
        Assert.Equal(expected, snapshot.Descriptor.Provenance.Any(item => item.Sha256 == Digest));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedProvenanceCannotSurviveMissingOrReplacedBytes(bool replace)
    {
        var snapshot = await Service().CaptureAsync("us-fda-ectd-3.2.2");
        var original = Assert.Single(snapshot.Resources, item => item.Key == Key);
        var resources = snapshot.Resources.Where(item => item.Key != Key).ToList();
        if (replace) resources.Add(StandardsSnapshotResource.Create(original.Key, original.Category, original.RelativePath, original.MediaType, [1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc));
    }

    [Fact]
    public async Task LegacySnapshotDoesNotUseCurrentGuideAsSubstitute()
    {
        var snapshot = await Service().CaptureAsync("us-fda-ectd-3.2.2");
        Assert.Contains(snapshot.Resources, item => item.Key == Key);
        var legacy = StandardsProfileSnapshot.Create(snapshot.Descriptor with
        { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != Digest).ToArray() },
            snapshot.Resources.Where(item => item.Key != Key), snapshot.AcquiredUtc);
        Assert.False(PackageStandardsContext.FromSnapshot(legacy).HasArchivedOfficialSource(Source));
    }

    [Fact]
    public async Task ReopenedSnapshotReadsArchivedGuideWithoutProvider()
    {
        var store = new InMemoryStandardsSnapshotStore();
        var service = Service(store);
        var snapshot = await service.CaptureAsync("us-fda-ectd-3.2.2");
        var reopened = new StandardsSnapshotService(store, new UnavailableProvider());
        var bytes = (await reopened.OpenResourceAsync(snapshot.Id, Key)).Bytes;
        Assert.Equal(Digest, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        bytes[0] ^= 0xff;
        Assert.Equal(Digest, Convert.ToHexString(SHA256.HashData((await reopened.OpenResourceAsync(snapshot.Id, Key)).Bytes)).ToLowerInvariant());
    }

    private static StandardsSnapshotService Service(InMemoryStandardsSnapshotStore? store = null, int day = 20) =>
        new(store ?? new(), new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]), clock: new Clock(day));
    private sealed class Clock(int day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, day, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class UnavailableProvider : IStandardsProfileProvider
    {
        public StandardsProfile GetProfile(string templateKey) => throw new InvalidOperationException("Archived resources must not consult the current provider.");
    }
}
