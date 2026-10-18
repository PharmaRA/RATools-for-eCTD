using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class ExternalComparisonReportTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static ValidationBinding Binding() => PackageValidationRunTests.Binding();

    [Fact]
    public void EvidenceBindsTheExactPackageAndRequiresExplainedDifferences()
    {
        var binding = Binding();
        var evidence = new ExternalComparisonReport(Guid.NewGuid(), binding, "eValidator", "22.1", binding.InputManifestDigest,
            "<report/>", [new("EXT-1", "FILE-HREF", "Missing href", "Both engines resolve the same missing payload.")], Now);
        Assert.Equal(binding.InputManifestDigest, evidence.PackageDigest);
        Assert.Equal(64, evidence.ReportSha256.Length);
        Assert.Equal(64, evidence.Digest.Length);
        Assert.Single(evidence.Differences);

        var other = new ExternalComparisonReport(Guid.NewGuid(), binding, "eValidator", "22.1", binding.InputManifestDigest,
            "<report/>", [new("EXT-1", "FILE-HREF", "Missing href", "Reviewed and accepted for this scope.")], Now);
        Assert.NotEqual(evidence.Digest, other.Digest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyExplanationsAreRejected(string explanation)
    {
        var binding = Binding();
        Assert.Throws<ArgumentException>(() => new ExternalComparisonReport(Guid.NewGuid(), binding, "tool", "1",
            binding.InputManifestDigest, "<report/>", [new("EXT-1", null, "difference", explanation)], Now));
    }

    [Fact]
    public void EvidenceForDifferentBytesOrWithoutContentIsRejected()
    {
        var binding = Binding();
        Assert.Throws<ArgumentException>(() => new ExternalComparisonReport(Guid.NewGuid(), binding, "tool", "1",
            new string('c', 64), "<report/>", [], Now));
        Assert.Throws<ArgumentException>(() => new ExternalComparisonReport(Guid.NewGuid(), binding, "tool", "1",
            binding.InputManifestDigest, "  ", [], Now));
        Assert.Throws<ArgumentException>(() => new ExternalComparisonReport(Guid.Empty, binding, "tool", "1",
            binding.InputManifestDigest, "<report/>", [], Now));
        Assert.Throws<ArgumentException>(() => new ExternalComparisonReport(Guid.NewGuid(), binding, "tool", "1",
            binding.InputManifestDigest, "<report/>", [], Now.ToLocalTime()));
    }
}
