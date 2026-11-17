using System.IO.Compression;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRegionalFilePresenceTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() =>
        new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));

    [Theory]
    [InlineData("missing", false, CheckStatus.Fail)]
    [InlineData("missing", true, CheckStatus.Fail)]
    [InlineData("present", false, CheckStatus.Pass)]
    [InlineData("present", true, CheckStatus.Pass)]
    [InlineData("empty", false, CheckStatus.Pass)]
    [InlineData("empty", true, CheckStatus.Pass)]
    [InlineData("wrong-path", false, CheckStatus.Fail)]
    [InlineData("wrong-path", true, CheckStatus.Fail)]
    [InlineData("wrong-case", false, CheckStatus.Fail)]
    [InlineData("wrong-case", true, CheckStatus.Fail)]
    [InlineData("surrounding", false, CheckStatus.Fail)]
    [InlineData("surrounding", true, CheckStatus.Fail)]
    [InlineData("history", false, CheckStatus.Fail)]
    [InlineData("history", true, CheckStatus.Fail)]
    [InlineData("directory", false, CheckStatus.Fail)]
    [InlineData("directory", true, CheckStatus.Fail)]
    public async Task OnlyExactTargetFileEstablishesPresence(string variant, bool zip, CheckStatus expected)
    {
        var result = await Inspect(variant, zip);
        Assert.Equal(PackageDeliveryInspector.VersionFor(new PdfPigPackageInspector().EngineVersion), result.EngineVersion);
        var check = Check(result);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Empty(finding.Location.Files!);
        }
        else
        {
            Assert.Empty(check.Findings);
            Assert.Contains(result.Checks.SelectMany(item => item.Findings), finding =>
                finding.RuleId is "XML-OFFLINE" or "PROFILE-VERSION" or "US-REGIONAL-DTD" &&
                finding.CheckStatus == CheckStatus.Fail && finding.Location.LogicalPath == "0001/m1/us/us-regional.xml");
        }
    }

    [Theory]
    [InlineData("2017-04-01", CheckStatus.NotApplicable)]
    [InlineData("2017-04-02", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task ExplicitEffectiveDateIsRequired(string? date, CheckStatus expected)
        => Assert.Equal(expected, Check(await Inspect("missing", date: date)).Status);

    [Theory]
    [InlineData("missing-source", CheckStatus.NotEvaluated)]
    [InlineData("old-catalog", null)]
    [InlineData("ich-profile", null)]
    public async Task ArchivedScopeIsNotSilentlyUpgraded(string mode, CheckStatus? expected)
    {
        var result = await Inspect("missing", mode: mode);
        if (expected is null) Assert.DoesNotContain(result.Checks, check => check.RuleId == "FDA47-2");
        else Assert.Equal(expected, Check(result).Status);
    }

    [Fact]
    public async Task ExhaustedSharedBudgetCannotPassMissingFile()
    {
        var result = await Inspect("budget", limits: new() { MaxXmlFindings = 2 });
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
        Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
    }

    private static RuleCheckResult Check(PackageDeliveryInspection result) => Assert.Single(result.Checks, check => check.RuleId == "FDA47-2");
    private static async Task<PackageDeliveryInspection> Inspect(string variant, bool zip = false, string? date = "2026-11-17", string mode = "valid", PackageReadLimits? limits = null)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var source = Path.Combine(root, "source");
            var sequence = Path.Combine(source, "0001");
            Directory.CreateDirectory(sequence);
            await File.WriteAllTextAsync(Path.Combine(sequence, "index.xml"), """
                <!DOCTYPE ectd:ectd SYSTEM "util/dtd/ich-ectd-3-2.dtd">
                <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.2">
                <m1-administrative-information-and-prescribing-information>
                <leaf ID="regional-0001" operation="new" checksum="00000000000000000000000000000000" checksum-type="md5" xlink:href="m1/us/us-regional.xml"><title>Regional backbone</title></leaf>
                </m1-administrative-information-and-prescribing-information></ectd:ectd>
                """);
            var path = variant switch
            {
                "wrong-path" => "0001/us-regional.xml",
                "wrong-case" => "0001/m1/us/US-REGIONAL.xml",
                "surrounding" or "history" => "0000/m1/us/us-regional.xml",
                _ => "0001/m1/us/us-regional.xml"
            };
            if (variant is not ("missing" or "budget"))
            {
                var full = Path.Combine(source, path);
                if (variant == "directory") Directory.CreateDirectory(full);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await File.WriteAllTextAsync(full, variant == "empty" ? "" : "<not-valid-regional />");
                }
            }
            if (variant == "history") await File.WriteAllTextAsync(Path.Combine(source, "0000/index.xml"), "<ectd />");
            if (variant == "budget") await File.WriteAllTextAsync(Path.Combine(sequence, "bad~name.txt"), "consume earlier filename finding budget");
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
                    catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-2")), context.Assets, context.NodeSchema, context.SnapshotId, selectionDate: context.SelectionDate);
            }
            var app = Guid.NewGuid();
            await using var capture = await new PackageInputReader(Path.Combine(root, "validation/captures"))
                .ReadAsync(new(app, "directory-input", source, "0001"), limits ?? new());
            await using var history = variant == "history" ? await new PackageInputReader(Path.Combine(root, "validation/captures"))
                .ReadAsync(new(app, "history-input", source, "0000"), limits ?? new()) : null;
            var baseline = new HistoryBaselineManifest(app, "0001", history is null ? [] :
                [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]);
            return await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: context)
                .InspectAsync(new(new(capture, baseline, history is null ? [] : [history])), mode == "ich-profile" ? PackageValidationCatalog.IchProfile : PackageValidationCatalog.UsProfile);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }
}
