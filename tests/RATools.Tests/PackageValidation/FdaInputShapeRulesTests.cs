using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaInputShapeRulesTests
{
    [Theory]
    [InlineData(PackageInputRole.SingleFile, 1, "FDA47-3", CheckStatus.Fail)]
    [InlineData(PackageInputRole.SingleFile, 1, "FDA47-4", CheckStatus.Pass)]
    [InlineData(PackageInputRole.Directory, 1, "FDA47-3", CheckStatus.Pass)]
    [InlineData(PackageInputRole.ZipContainer, 1, "FDA47-3", CheckStatus.Pass)]
    [InlineData(PackageInputRole.Directory, 0, "FDA47-3", CheckStatus.Pass)]
    [InlineData(PackageInputRole.ZipContainer, 0, "FDA47-3", CheckStatus.Pass)]
    [InlineData(PackageInputRole.Directory, 0, "FDA47-4", CheckStatus.Fail)]
    [InlineData(PackageInputRole.ZipContainer, 0, "FDA47-4", CheckStatus.Fail)]
    [InlineData(PackageInputRole.Directory, 1, "FDA47-4", CheckStatus.Pass)]
    [InlineData(PackageInputRole.ZipContainer, 1, "FDA47-4", CheckStatus.Pass)]
    public async Task ClassifiesSubmissionShapeRatherThanSingleMemberCount(PackageInputRole role, int count, string id, CheckStatus expected)
    {
        var context = await Context();
        var evidence = Evidence(role, count);
        var check = Assert.Single(FdaInputShapeRules.Inspect(evidence, PackageValidationCatalog.UsProfile, context, 5), item => item.RuleId == id);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Equal(ValidationSeverity.Error, Assert.Single(check.Findings).Severity);
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2016-05-30", false, 5, CheckStatus.NotApplicable)]
    [InlineData("2016-05-31", false, 5, CheckStatus.Fail)]
    [InlineData(null, false, 5, CheckStatus.NotEvaluated)]
    [InlineData("2026-11-19", true, 5, CheckStatus.NotEvaluated)]
    [InlineData("2026-11-19", false, 0, CheckStatus.NotEvaluated)]
    public async Task RequiresSourceDateAndFindingBudget(string? date, bool missingSource, int remaining, CheckStatus expected)
    {
        var context = await Context(date, missingSource);
        var check = Assert.Single(FdaInputShapeRules.Inspect(Evidence(PackageInputRole.Directory, 0), PackageValidationCatalog.UsProfile, context, remaining), item => item.RuleId == "FDA47-4");
        Assert.Equal(expected, check.Status);
    }

    [Fact]
    public async Task NonFdaProfileDoesNotAcquireOfficialResults()
        => Assert.Empty(FdaInputShapeRules.Inspect(Evidence(PackageInputRole.Directory, 0), PackageValidationCatalog.IchProfile, await Context(), 5));

    internal static PackageInputShapeEvidence Evidence(PackageInputRole role, int count) => new(Guid.NewGuid(), "shape", role,
        count == 0 ? [] : [new("document.txt", 0, new string('a', 64), new string('b', 32))], ["empty"], role == PackageInputRole.ZipContainer ? new string('c', 64) : null, new PackageReadLimits().Digest());

    private static async Task<PackageStandardsContext> Context(string? date = "2026-11-19", bool missingSource = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingSource)
        {
            const string sha = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != sha).ToArray() },
                snapshot.Resources.Where(item => item.Sha256 != sha), snapshot.AcquiredUtc);
        }
        return PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.ParseExact(date, "yyyy-MM-dd"));
    }
}
