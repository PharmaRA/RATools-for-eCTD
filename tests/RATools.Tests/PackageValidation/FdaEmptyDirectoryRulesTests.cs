using System.IO.Compression;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaEmptyDirectoryRulesTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() =>
        new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));

    [Theory]
    [InlineData("empty", false, CheckStatus.Fail, "0001/empty")]
    [InlineData("nested", false, CheckStatus.Fail, "0001/parent/child")]
    [InlineData("file", false, CheckStatus.Pass, null)]
    [InlineData("outside", false, CheckStatus.Pass, null)]
    [InlineData("empty", true, CheckStatus.Fail, "0001/empty")]
    [InlineData("nested", true, CheckStatus.Fail, "0001/parent/child")]
    [InlineData("file", true, CheckStatus.Pass, null)]
    [InlineData("outside", true, CheckStatus.Pass, null)]
    public async Task OnlyChildlessTargetDirectoriesAreLocated(string variant, bool zip, CheckStatus expected, string? path)
    {
        var result = await Inspect(variant, zip);
        var check = Check(result);
        Assert.Equal(expected, check.Status);
        if (path is not null)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(path, finding.Location.LogicalPath);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Empty(finding.Location.Files!);
        }
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task SelectionDateControlsApplicability(string? date, CheckStatus expected)
        => Assert.Equal(expected, Check(await Inspect("empty", date: date)).Status);

    [Theory]
    [InlineData("missing-source", CheckStatus.NotEvaluated)]
    [InlineData("old-catalog", null)]
    [InlineData("ich-profile", null)]
    public async Task ArchivedScopeCannotBeSilentlyUpgraded(string mode, CheckStatus? expected)
    {
        var result = await Inspect("empty", mode: mode);
        if (expected is null) Assert.DoesNotContain(result.Checks, check => check.RuleId == "FDA47-1322");
        else Assert.Equal(expected, Check(result).Status);
    }

    [Fact]
    public async Task DirectoryFindingsShareTheFileStageBudget()
    {
        var result = await Inspect("many", limits: new() { MaxXmlFindings = 2 });
        Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
        Assert.Contains(result.Files.Checks, check => check.Status == CheckStatus.NotEvaluated);
    }

    private static RuleCheckResult Check(PackageDeliveryInspection result) => Assert.Single(result.Checks, check => check.RuleId == "FDA47-1322");
    private static async Task<PackageDeliveryInspection> Inspect(string variant, bool zip = false, string? date = "2026-11-17", string mode = "valid", PackageReadLimits? limits = null)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var source = Path.Combine(root, "source");
            var sequence = Path.Combine(source, "0001");
            Directory.CreateDirectory(sequence);
            await File.WriteAllTextAsync(Path.Combine(sequence, "index.xml"), "<ectd />");
            var folder = variant switch { "nested" => "0001/parent/child", "outside" => "outside/empty", _ => "0001/empty" };
            Directory.CreateDirectory(Path.Combine(source, folder));
            if (variant == "file") await File.WriteAllTextAsync(Path.Combine(source, folder, "leaf.txt"), "bytes");
            if (variant == "many") Directory.CreateDirectory(Path.Combine(sequence, "another-empty"));
            if (zip) { var archive = Path.Combine(root, "input.zip"); ZipFile.CreateFromDirectory(source, archive); source = archive; }
            var snapshot = await Snapshot.Value;
            if (mode == "missing-source")
            {
                const string sha = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
                snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != sha).ToArray() },
                    snapshot.Resources.Where(item => item.Sha256 != sha), snapshot.AcquiredUtc);
            }
            var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.ParseExact(date, "yyyy-MM-dd"));
            if (mode == "old-catalog")
            {
                var catalog = context.Catalog;
                context = new(new ValidationRuleCatalog(catalog.Version, catalog.Digest, catalog.Sources.Values, catalog.Profiles.Values,
                    catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1322")), context.Assets, context.NodeSchema, context.SnapshotId, selectionDate: context.SelectionDate);
            }
            var app = Guid.NewGuid();
            await using var capture = await new PackageInputReader(Path.Combine(root, "validation/captures"))
                .ReadAsync(new(app, "directory-input", source, "0001"), limits ?? new());
            return await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: context)
                .InspectAsync(new(new(capture, new HistoryBaselineManifest(app, "0001", []), [])), mode == "ich-profile" ? PackageValidationCatalog.IchProfile : PackageValidationCatalog.UsProfile);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }
}
