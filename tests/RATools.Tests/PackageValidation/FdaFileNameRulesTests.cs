using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaFileNameRulesTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() =>
        new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));

    [Theory]
    [InlineData(60, CheckStatus.Pass)]
    [InlineData(61, CheckStatus.Fail)]
    public async Task FilenameLengthIncludesTheExtension(int stemLength, CheckStatus expected)
    {
        var result = await Inspect(new string('a', stemLength) + ".pdf", new(2026, 11, 17));
        var check = Assert.Single(result.Checks, item => item.RuleId == "FDA47-1221");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.EndsWith(new string('a', stemLength) + ".pdf", finding.Location.LogicalPath);
            Assert.NotEmpty(finding.Location.Files!);
        }
    }

    [Theory]
    [InlineData("leaf", CheckStatus.Fail)]
    [InlineData("leaf.pdf", CheckStatus.Pass)]
    [InlineData("leaf.tar.gz", CheckStatus.Pass)]
    [InlineData(".hidden", CheckStatus.Fail)]
    public async Task MissingExtensionUsesTheCapturedFileName(string name, CheckStatus expected)
    {
        var check = Assert.Single((await Inspect(name, new(2026, 11, 17))).Checks, item => item.RuleId == "FDA47-1298");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Equal(ValidationSeverity.Warning, Assert.Single(check.Findings).Severity);
    }

    [Theory]
    [InlineData("1221", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("1221", "2008-03-10", CheckStatus.Fail)]
    [InlineData("1298", "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("1298", "2013-04-30", CheckStatus.Fail)]
    [InlineData("1221", null, CheckStatus.NotEvaluated)]
    [InlineData("1298", null, CheckStatus.NotEvaluated)]
    public async Task ExplicitSelectionDateGatesEachRule(string id, string? date, CheckStatus expected)
        => Assert.Equal(expected, Assert.Single((await Inspect(new string('a', 65), date is null ? null : DateOnly.ParseExact(date, "yyyy-MM-dd"))).Checks,
            item => item.RuleId == "FDA47-" + id).Status);

    [Theory]
    [InlineData("version")]
    [InlineData("digest")]
    [InlineData("unverified")]
    [InlineData("snapshot")]
    public async Task MissingAuthorizationCannotUseCurrentSourceFiles(string mutation)
    {
        var original = PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(2026, 11, 17));
        var catalog = original.Catalog;
        var changed = new ValidationRuleCatalog(catalog.Version, catalog.Digest, catalog.Sources.Values.Select(source =>
            source.Id != "fda-validation-criteria-4.7" ? source : mutation switch
            {
                "version" => source with { Version = "4.5" },
                "digest" => source with { Sha256 = new string('a', 64) },
                "unverified" => source with { VerificationStatus = "Unverified" },
                _ => source
            }), catalog.Profiles.Values, catalog.Rules.Values);
        var context = new PackageStandardsContext(changed, original.Assets, original.NodeSchema,
            mutation == "snapshot" ? null : original.SnapshotId, selectionDate: original.SelectionDate);
        var result = await Inspect(new string('a', 65), original.SelectionDate, context);
        Assert.All(result.Checks.Where(item => item.RuleId is "FDA47-1221" or "FDA47-1298"), item => Assert.Equal(CheckStatus.NotEvaluated, item.Status));
    }

    [Fact]
    public async Task IntegrityValidSnapshotWithoutTheOfficialOriginalCannotAuthorizeRules()
    {
        var snapshot = await Snapshot.Value;
        const string digest = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
        var descriptor = snapshot.Descriptor with
        {
            Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != digest).ToArray()
        };
        var legacy = StandardsProfileSnapshot.Create(descriptor, snapshot.Resources.Where(item => item.Sha256 != digest), snapshot.AcquiredUtc);
        legacy.Verify();
        var result = await Inspect(new string('a', 65), new(2026, 11, 17), PackageStandardsContext.FromSnapshot(legacy, selectionDate: new(2026, 11, 17)));
        foreach (var id in new[] { "FDA47-1221", "FDA47-1298" })
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == id).Status);
    }

    [Fact]
    public async Task OfficialAndProjectFileChecksShareTheFindingLimit()
    {
        var result = await Inspect(new string('a', 65), new(2026, 11, 17), limits: new() { MaxXmlFindings = 2 });
        Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
        Assert.Contains(result.Files.Checks, check => check.Status == CheckStatus.NotEvaluated || check.Findings.Any(finding => finding.CheckStatus == CheckStatus.NotEvaluated));
    }

    [Fact]
    public async Task APreviousCatalogAndAnIchProfileNeverAcquireNewFdaChecks()
    {
        var original = PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(2026, 11, 17));
        var catalog = original.Catalog;
        var legacy = new ValidationRuleCatalog(catalog.Version, catalog.Digest, catalog.Sources.Values, catalog.Profiles.Values,
            catalog.Rules.Values.Where(rule => rule.InternalRuleId is not ("FDA47-1221" or "FDA47-1298")));
        var context = new PackageStandardsContext(legacy, original.Assets, original.NodeSchema, original.SnapshotId, selectionDate: original.SelectionDate);
        Assert.DoesNotContain((await Inspect("leaf", original.SelectionDate, context)).Checks, item => item.RuleId is "FDA47-1221" or "FDA47-1298");
        Assert.DoesNotContain((await Inspect("leaf", original.SelectionDate, original, PackageValidationCatalog.IchProfile)).Checks, item => item.RuleId is "FDA47-1221" or "FDA47-1298");
    }

    private static async Task<PackageDeliveryInspection> Inspect(string name, DateOnly? date,
        PackageStandardsContext? context = null, string profile = PackageValidationCatalog.UsProfile, PackageReadLimits? limits = null)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var source = Path.Combine(root, "source", "0001");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "index.xml"), "<ectd />");
            await File.WriteAllTextAsync(Path.Combine(source, name), "synthetic bytes");
            var app = Guid.NewGuid();
            await using var captured = await new PackageInputReader(Path.Combine(root, "validation", "captures"))
                .ReadAsync(new(app, "filename-input", source, "0001"), limits ?? new());
            var input = new PackageDeliveryInput(new(captured, new HistoryBaselineManifest(app, "0001", []), []));
            return await new PackageDeliveryInspector(new PdfPigPackageInspector(),
                standards: context ?? PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: date))
                .InspectAsync(input, profile);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }
}
