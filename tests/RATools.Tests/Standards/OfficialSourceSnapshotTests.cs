using System.Security.Cryptography;
using System.Text.Json;
using RATools.Application.Standards;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Standards;

public sealed class OfficialSourceSnapshotTests
{
    private static readonly string[] FutureFdaCriteria = ["2024", "2025", "2026"];

    [Theory]
    [InlineData("us-fda-ectd-3.2.2", "official-fda-criteria", "4.7", "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c", 885718)]
    [InlineData("eu-ectd-3.2.2", "official-ema-criteria", "8.2", "0f64339496564d89abe9e96376f29805dce2302a56f54ce12858d083315d1fd9", 71307)]
    public async Task CurrentCaptureBindsRegulatoryCriteriaBytesAndVerifiedProvenance(
        string template, string key, string criteriaVersion, string digest, long length)
    {
        var service = CreateService();
        var snapshot = await service.CaptureAsync(template);
        var criteria = Assert.Single(snapshot.Resources, resource => resource.Key == key);
        Assert.Equal("OfficialSource", criteria.Category);
        Assert.Equal(criteriaVersion, snapshot.Descriptor.CriteriaVersion);
        Assert.Equal(digest, criteria.Sha256);
        Assert.Equal(length, criteria.Length);
        var provenance = Assert.Single(snapshot.Descriptor.Provenance, source => source.Sha256 == digest);
        Assert.Equal(criteriaVersion, provenance.Version);
        Assert.Equal("Verified", provenance.VerificationStatus);
        Assert.Equal(new DateOnly(2026, 11, 9), provenance.AcquiredOn);
        Assert.Equal(new DateOnly(2026, 11, 9), provenance.VerifiedOn);
        var sourceEvidence = Assert.Single(snapshot.Descriptor.Evidence, item => item.Category == "Sources");
        Assert.Equal("Verified", sourceEvidence.Status);
        Assert.Contains(snapshot.Resources, resource => resource.Category == "Sources" && resource.Sha256 == sourceEvidence.EvidenceSha256);
        Assert.All(snapshot.Descriptor.Provenance, source =>
            Assert.Contains(snapshot.Resources, resource => resource.Category == "OfficialSource" && resource.Sha256 == source.Sha256));
        Assert.Equal(digest, Sha((await service.OpenResourceAsync(snapshot.Id, key)).Bytes));
        Assert.Equal(snapshot.Id, (await service.CaptureAsync(template)).Id);
    }

    [Fact]
    public async Task CapturedApplicabilityKeepsFutureFdaRuleDatesSeparateFromDocumentRevision()
    {
        var snapshot = await CreateService().CaptureAsync("us-fda-ectd-3.2.2");
        var inventory = Assert.Single(snapshot.Resources, resource => resource.Key == "official-source-inventory");
        Assert.Equal("67cfb2e3d35631945cd8ba66c0deed6044ce26bc9bd514411b4953caad24ba84", inventory.Sha256);
        using var json = JsonDocument.Parse(inventory.ReadBytes());
        var fda = json.RootElement.GetProperty("applicability").EnumerateArray().Single(region => region.GetProperty("region").GetString() == "FDA");
        Assert.Equal("2026-10-16", fda.GetProperty("documentRevisionDate").GetString());
        var future = Assert.Single(fda.GetProperty("futureCriteria").EnumerateArray());
        Assert.Equal("2027-01-16", future.GetProperty("effectiveOn").GetString());
        Assert.Equal(FutureFdaCriteria, future.GetProperty("ids").EnumerateArray().Select(value => value.GetString()));
    }

    [Theory]
    [InlineData("us-fda-ectd-3.2.2")]
    [InlineData("eu-ectd-3.2.2")]
    public async Task VerifiedSourcesDoNotQualifyUnimplementedRulesOrExternalComparison(string template)
    {
        var snapshot = await CreateService().CaptureAsync(template);
        Assert.Equal("Verified", Assert.Single(snapshot.Descriptor.Evidence, item => item.Category == "Sources").Status);
        var reasons = snapshot.Evaluate(null, new DateOnly(2026, 11, 9), template);
        Assert.DoesNotContain(reasons, reason => reason.StartsWith("SourceVerificationIncomplete:", StringComparison.Ordinal));
        Assert.DoesNotContain("EvidenceIncomplete: Sources", reasons);
        Assert.Contains("EvidenceIncomplete: Rules", reasons);
        Assert.Contains("EvidenceIncomplete: ExternalComparison", reasons);
        Assert.Contains(reasons, reason => reason.StartsWith("ProfileNotActive:", StringComparison.Ordinal));
        Assert.All(snapshot.Descriptor.Scopes, scope => Assert.False(scope.Finalize));
    }

    [Fact]
    public async Task ReopenedCaptureReadsOriginalOfficialBytesWithoutConsultingCurrentProvider()
    {
        var store = new InMemoryStandardsSnapshotStore();
        var service = CreateService(store);
        var snapshot = await service.CaptureAsync("us-fda-ectd-3.2.2");
        var original = await service.OpenResourceAsync(snapshot.Id, "official-fda-criteria");
        var reopened = new StandardsSnapshotService(store, new UnavailableProvider());
        Assert.Equal(snapshot.Id, (await reopened.GetAsync(snapshot.Id)).Id);
        Assert.Equal(original.Bytes, (await reopened.OpenResourceAsync(snapshot.Id, original.Key)).Bytes);
        original.Bytes[0] ^= 0xff;
        Assert.Equal("2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c",
            Sha((await reopened.OpenResourceAsync(snapshot.Id, original.Key)).Bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedProvenanceRejectsMissingOrReplacedOfficialBytes(bool replace)
    {
        var snapshot = await CreateService().CaptureAsync("us-fda-ectd-3.2.2");
        var original = Assert.Single(snapshot.Resources, resource => resource.Key == "official-fda-criteria");
        var resources = snapshot.Resources.Where(resource => resource.Key != original.Key).ToList();
        if (replace)
            resources.Add(StandardsSnapshotResource.Create(original.Key, original.Category, original.RelativePath, original.MediaType, [1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc));
    }

    [Fact]
    public async Task NewSourceCaptureCannotBeBackdatedBeforeItsAcquisition()
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider(),
            clock: new BeforeAcquisitionClock());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CaptureAsync("us-fda-ectd-3.2.2"));
    }

    [Theory]
    [InlineData("Guide")]
    [InlineData("Criteria")]
    [InlineData("Region")]
    [InlineData("Reference")]
    public async Task DifferentProfileVersionsCannotBorrowTheCurrentOfficialSourceQualification(string field)
    {
        var current = new FdaEctd322StandardsProfileProvider().GetProfile("us-fda-ectd-3.2.2");
        var profile = field switch
        {
            "Guide" => current with { TechnicalConformanceGuideVersion = "unknown-guide" },
            "Criteria" => current with { ValidationCriteriaVersion = "unknown-criteria" },
            "Reference" => current with { OfficialReferences = [.. current.OfficialReferences, "https://example.test/unverified-standard"] },
            _ => current with { Region = "EU" }
        };
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FixedProfile(profile));
        var snapshot = await service.CaptureAsync(profile.TemplateKey);
        Assert.Equal("Unverified", Assert.Single(snapshot.Descriptor.Evidence, item => item.Category == "Sources").Status);
        Assert.All(snapshot.Descriptor.Provenance, source => Assert.Equal("Unverified", source.VerificationStatus));
    }

    [Theory]
    [InlineData("Guide")]
    [InlineData("Criteria")]
    [InlineData("Region")]
    public async Task ArchivedProfileRejectsAContradictorySnapshotVersionWithoutConsultingTheProvider(string field)
    {
        var snapshot = await CreateService().CaptureAsync("us-fda-ectd-3.2.2");
        var descriptor = field switch
        {
            "Guide" => snapshot.Descriptor with { GuideVersion = "different-guide" },
            "Criteria" => snapshot.Descriptor with { CriteriaVersion = "different-criteria" },
            _ => snapshot.Descriptor with { Region = "EU" }
        };
        var inconsistent = StandardsProfileSnapshot.Create(descriptor, snapshot.Resources, snapshot.AcquiredUtc);
        Assert.Throws<InvalidDataException>(() => StandardsSnapshotService.Profile(inconsistent));
    }

    private static StandardsSnapshotService CreateService(InMemoryStandardsSnapshotStore? store = null) =>
        new(store ?? new(), new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]));
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static async Task RetainCaptureEvidenceAsync(StandardsProfileSnapshot snapshot, string scenario)
    {
        var root = Path.GetFullPath(Path.Combine(".artifacts", "official-source-capture-evidence", Guid.NewGuid().ToString("N")));
        for (var current = root; current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Capture evidence must not traverse links.");
        Directory.CreateDirectory(root);
        var receipt = new
        {
            scenario, snapshot.Id, snapshot.AcquiredUtc, snapshot.Descriptor, snapshot.RuleProfileAlias,
            Resources = snapshot.Resources.Select(resource => new { resource.Key, resource.Category, resource.RelativePath, resource.MediaType, resource.Length, resource.Sha256 })
        };
        await using var output = new FileStream(Path.Combine(root, "capture.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(output, receipt);
    }
    private sealed class UnavailableProvider : IStandardsProfileProvider
    {
        public StandardsProfile GetProfile(string templateKey) => throw new InvalidOperationException("Current provider must not be read for archived content.");
    }
    private sealed class FixedProfile(StandardsProfile profile) : IStandardsProfileProvider
    {
        public StandardsProfile GetProfile(string templateKey) => profile;
    }
    private sealed class BeforeAcquisitionClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
