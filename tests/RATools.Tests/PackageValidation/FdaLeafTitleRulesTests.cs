using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaLeafTitleRulesTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() =>
        new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));

    [Theory]
    [InlineData("Title", CheckStatus.Pass, CheckStatus.Pass)]
    [InlineData(" Title", CheckStatus.Fail, CheckStatus.Pass)]
    [InlineData("Title ", CheckStatus.Fail, CheckStatus.Pass)]
    [InlineData("", CheckStatus.Pass, CheckStatus.Fail)]
    [InlineData(" ", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated, CheckStatus.Fail)]
    public async Task RawLeafTitlePreservesSpacesAndMissingValues(string? title, CheckStatus spaces, CheckStatus required)
    {
        var result = await Inspect(title);
        Assert.Equal(spaces, Check(result, "1276").Status);
        Assert.Equal(required, Check(result, "1289").Status);
        if (spaces == CheckStatus.Fail) Assert.Equal(ValidationSeverity.Info, Assert.Single(Check(result, "1276").Findings).Severity);
        if (required == CheckStatus.Fail) Assert.Equal(ValidationSeverity.Warning, Assert.Single(Check(result, "1289").Findings).Severity);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    public async Task AmbiguousOrUnreadableTitlesRemainUnevaluated(string variant)
    {
        var result = await Inspect("Title", variant);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "1276").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "1289").Status);
    }

    [Theory]
    [InlineData("2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("2008-03-10", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task SelectionDateIsBound(string? date, CheckStatus expected)
    {
        var result = await InspectCore(" ", date: date is null ? null : DateOnly.ParseExact(date, "yyyy-MM-dd"));
        Assert.Equal(expected, Check(result, "1276").Status);
        Assert.Equal(expected, Check(result, "1289").Status);
    }

    [Fact]
    public async Task AnUnreadRegionalBackboneCannotBecomeAPassingTitleCheck()
    {
        var result = await InspectCore("Title", date: new(2026, 11, 17), limits: new() { MaxBackbones = 1 });
        Assert.Single(result.Lifecycle.XmlInspections.Single().Backbones);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "1276").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "1289").Status);
    }

    [Fact]
    public async Task LeafChecksRespectTheSharedFileFindingLimit()
    {
        var result = await InspectCore(" ", date: new(2026, 11, 17), limits: new() { MaxXmlFindings = 2 });
        Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
        Assert.Contains(result.Files.Checks, check => check.Status == CheckStatus.NotEvaluated);
    }

    private static RuleCheckResult Check(PackageDeliveryInspection result, string id) => Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);
    private static Task<PackageDeliveryInspection> Inspect(string? title, string variant = "valid") => InspectCore(title, variant, new DateOnly(2026, 11, 17));
    private static async Task<PackageDeliveryInspection> InspectCore(string? title, string variant = "valid", DateOnly? date = null, PackageReadLimits? limits = null)
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var app = Guid.NewGuid();
            var memory = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            var source = Path.Combine(root, "source");
            foreach (var file in memory.Manifest.Files)
            {
                var path = Path.Combine(source, file.LogicalPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input = memory.OpenRead(file.LogicalPath); using var output = File.Create(path); input.CopyTo(output);
            }
            var index = Path.Combine(source, "0001", "index.xml");
            var xml = XDocument.Load(index, LoadOptions.PreserveWhitespace);
            var leaf = xml.Descendants("leaf").Single();
            leaf.Elements("title").Remove();
            if (title is not null) leaf.Add(new XElement("title", title));
            if (variant == "duplicate") leaf.Add(new XElement("title", "Other"));
            xml.Save(index);
            if (variant == "truncated") await File.WriteAllTextAsync(index, "<ectd>");
            await using var capture = await new PackageInputReader(Path.Combine(root, "validation", "captures"))
                .ReadAsync(new(app, "title-input", Path.Combine(source, "0001"), "0001"), limits ?? new());
            return await new PackageDeliveryInspector(new PdfPigPackageInspector(),
                standards: PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: date))
                .InspectAsync(new(new(capture, new HistoryBaselineManifest(app, "0001", []), [])), PackageValidationCatalog.UsProfile);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }
}
