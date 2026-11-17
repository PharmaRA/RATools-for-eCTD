using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaLeafOperationRulesTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() =>
        new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));

    [Theory]
    [InlineData(null, CheckStatus.Fail)]
    [InlineData("", CheckStatus.Fail)]
    [InlineData(" ", CheckStatus.Fail)]
    [InlineData("new", CheckStatus.Pass)]
    [InlineData("append", CheckStatus.Pass)]
    [InlineData("replace", CheckStatus.Pass)]
    [InlineData("delete", CheckStatus.Pass)]
    [InlineData("newish", CheckStatus.NotEvaluated)]
    public async Task ExplicitOperationIsRequiredWithoutGuessingUnknownValues(string? operation, CheckStatus expected)
    {
        var result = await Inspect(operation);
        Assert.Equal(expected, Check(result).Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(Check(result).Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("@operation", finding.Location.FieldPath);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
            Assert.Equal("regional", finding.Location.LeafId);
        }
    }

    [Theory]
    [InlineData("namespace", CheckStatus.Fail)]
    [InlineData("truncated", CheckStatus.NotEvaluated)]
    [InlineData("regional-unread", CheckStatus.NotEvaluated)]
    [InlineData("known-failure-unread", CheckStatus.Fail)]
    [InlineData("missing-source", CheckStatus.NotEvaluated)]
    [InlineData("old-catalog", null)]
    [InlineData("ich-profile", null)]
    public async Task SourceAndCoverageCannotSilentlyAuthorizeMissingChecks(string variant, CheckStatus? expected)
    {
        var result = await Inspect(variant == "regional-unread" ? "new" : null, variant);
        if (expected is null) Assert.DoesNotContain(result.Checks, item => item.RuleId == "FDA47-1034");
        else Assert.Equal(expected, Check(result).Status);
    }

    [Theory]
    [InlineData("2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("2008-03-10", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateAndExplicitSelectionAreRequired(string? date, CheckStatus expected)
        => Assert.Equal(expected, Check(await Inspect(null, date: date)).Status);

    [Fact]
    public async Task InternalDtdCannotSupplyAnOperationOrAuthorizeAPass()
    {
        var result = await Inspect(null, "defaulted");
        Assert.Contains(result.Lifecycle.XmlInspections.Single().Checks, item => item.RuleId == "XML-OFFLINE" && item.Status == CheckStatus.Fail);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
    }

    [Fact]
    public async Task ARegionalLeafHasItsOwnLocatedMissingOperation()
    {
        var check = Check(await Inspect("new", "regional"));
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
        Assert.Equal("form-leaf", finding.Location.LeafId);
    }

    [Fact]
    public async Task EarlierFileFindingsCannotSpendTheOperationBudgetAgain()
    {
        var result = await Inspect(null, "budget");
        Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
    }

    private static RuleCheckResult Check(PackageDeliveryInspection result) => Assert.Single(result.Checks, item => item.RuleId == "FDA47-1034");
    private static async Task<PackageDeliveryInspection> Inspect(string? operation, string variant = "valid", string? date = "2026-11-17")
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
            var index = Path.Combine(source, "0001/index.xml");
            var xml = XDocument.Load(index, LoadOptions.PreserveWhitespace);
            var leaf = xml.Descendants("leaf").Single();
            leaf.SetAttributeValue("operation", operation);
            if (variant == "namespace") leaf.SetAttributeValue(XName.Get("operation", "urn:unrelated"), "new");
            if (variant == "defaulted")
            {
                var type = xml.DocumentType!;
                type.ReplaceWith(new XDocumentType(type.Name, type.PublicId, type.SystemId, "<!ATTLIST leaf operation (new|append|replace|delete) 'new'>"));
            }
            if (variant == "budget") leaf.Element("title")!.Value = " ";
            xml.Save(index);
            if (variant == "regional")
            {
                var path = Path.Combine(source, "0001/m1/us/us-regional.xml");
                var regional = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                regional.Descendants("form").First().Add(new XElement("leaf", new XAttribute("ID", "form-leaf"), new XElement("title", "Form")));
                regional.Save(path);
            }
            if (variant == "truncated") await File.WriteAllTextAsync(index, "<ectd>");
            var snapshot = await Snapshot.Value;
            if (variant == "missing-source")
            {
                const string sha = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
                snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != sha).ToArray() },
                    snapshot.Resources.Where(item => item.Sha256 != sha), snapshot.AcquiredUtc);
            }
            var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.ParseExact(date, "yyyy-MM-dd"));
            if (variant == "old-catalog")
            {
                var catalog = context.Catalog;
                context = new(new ValidationRuleCatalog(catalog.Version, catalog.Digest, catalog.Sources.Values, catalog.Profiles.Values,
                    catalog.Rules.Values.Where(rule => rule.InternalRuleId != "FDA47-1034")), context.Assets, context.NodeSchema, context.SnapshotId, selectionDate: context.SelectionDate);
            }
            var limits = new PackageReadLimits { MaxBackbones = variant is "regional-unread" or "known-failure-unread" ? 1 : 100,
                MaxXmlFindings = variant == "budget" ? 2 : 10_000 };
            await using var capture = await new PackageInputReader(Path.Combine(root, "validation/captures"))
                .ReadAsync(new(app, "operation-input", Path.Combine(source, "0001"), "0001"), limits);
            return await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: context)
                .InspectAsync(new(new(capture, new HistoryBaselineManifest(app, "0001", []), [])),
                    variant == "ich-profile" ? PackageValidationCatalog.IchProfile : PackageValidationCatalog.UsProfile);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }
}
