using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaExtraneousLeafPathRulesTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() =>
        new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));

    [Theory]
    [InlineData("delete", "m2/file.pdf", null, "FDA47-1051", CheckStatus.Fail)]
    [InlineData("delete", " ", null, "FDA47-1051", CheckStatus.Fail)]
    [InlineData("delete", "", null, "FDA47-1051", CheckStatus.Pass)]
    [InlineData("delete", null, null, "FDA47-1051", CheckStatus.Pass)]
    [InlineData("new", "m2/file.pdf", null, "FDA47-1051", CheckStatus.Pass)]
    [InlineData("append", "m2/file.pdf", null, "FDA47-1051", CheckStatus.Pass)]
    [InlineData("replace", "m2/file.pdf", null, "FDA47-1051", CheckStatus.Pass)]
    [InlineData("new", "m2/file.pdf", "../0000/index.xml#old", "FDA47-1068", CheckStatus.Fail)]
    [InlineData("new", "m2/file.pdf", " ", "FDA47-1068", CheckStatus.Fail)]
    [InlineData("new", "m2/file.pdf", "", "FDA47-1068", CheckStatus.Pass)]
    [InlineData("new", "m2/file.pdf", null, "FDA47-1068", CheckStatus.Pass)]
    [InlineData("replace", "m2/file.pdf", "../0000/index.xml#old", "FDA47-1068", CheckStatus.Pass)]
    [InlineData("append", "m2/file.pdf", "../0000/index.xml#old", "FDA47-1068", CheckStatus.Pass)]
    [InlineData("delete", null, "../0000/index.xml#old", "FDA47-1068", CheckStatus.Pass)]
    [InlineData(null, "m2/file.pdf", "../0000/index.xml#old", "FDA47-1068", CheckStatus.NotEvaluated)]
    [InlineData("newish", "m2/file.pdf", "../0000/index.xml#old", "FDA47-1068", CheckStatus.NotEvaluated)]
    [InlineData("deleteish", "m2/file.pdf", null, "FDA47-1051", CheckStatus.NotEvaluated)]
    public async Task OperationDeterminesWhetherAPathIsExtraneous(string? operation, string? href, string? modified, string rule, CheckStatus expected)
    {
        var result = await Inspect(operation, href, modified);
        Assert.Equal(PackageDeliveryInspector.VersionFor(new PdfPigPackageInspector().EngineVersion), result.EngineVersion);
        var check = Check(result, rule);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal(rule == "FDA47-1051" ? "@xlink:href" : "@modified-file", finding.Location.FieldPath);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
            Assert.Equal("regional", finding.Location.LeafId);
            Assert.Equal(rule == "FDA47-1051" ? href : modified, finding.Actual);
        }
    }

    [Theory]
    [InlineData("missing-source", CheckStatus.NotEvaluated)]
    [InlineData("truncated", CheckStatus.NotEvaluated)]
    [InlineData("unread", CheckStatus.NotEvaluated)]
    [InlineData("known-failure-unread", CheckStatus.Fail)]
    [InlineData("old-catalog", null)]
    [InlineData("ich-profile", null)]
    public async Task IncompleteOrUnboundEvidenceCannotPass(string variant, CheckStatus? expected)
    {
        var result = await Inspect("delete", variant == "unread" ? null : "m2/file.pdf", null, variant);
        if (expected is null) Assert.DoesNotContain(result.Checks, item => item.RuleId is "FDA47-1051" or "FDA47-1068");
        else Assert.Equal(expected, Check(result, "FDA47-1051").Status);
    }

    [Theory]
    [InlineData("2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("2008-03-10", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateAndExplicitSelectionAreRequired(string? date, CheckStatus expected)
        => Assert.Equal(expected, Check(await Inspect("delete", "m2/file.pdf", null, date: date), "FDA47-1051").Status);

    [Fact]
    public async Task UnrelatedNamespacesDoNotCreateAnOfficialPathAttribute()
        => Assert.Equal(CheckStatus.Pass, Check(await Inspect("delete", null, null, "namespace"), "FDA47-1051").Status);

    [Fact]
    public async Task AlternativeXlinkPrefixReportsTheWrittenAttribute()
    {
        var check = Check(await Inspect("delete", "m2/file.pdf", null, "alias"), "FDA47-1051");
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("@link:href", finding.Location.FieldPath);
        Assert.Equal("m2/file.pdf", finding.Actual);
    }

    [Fact]
    public async Task RegionalLeafHasItsOwnLocatedExtraneousPath()
    {
        var check = Check(await Inspect("new", "m1/us/us-regional.xml", null, "regional"), "FDA47-1068");
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
        Assert.Equal("form-leaf", finding.Location.LeafId);
    }

    [Fact]
    public async Task EarlierFindingsCannotSpendThePathBudgetAgain()
    {
        var result = await Inspect("delete", "m2/file.pdf", null, "budget");
        Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "FDA47-1051").Status);
    }

    [Theory]
    [InlineData("1051")]
    [InlineData("1068")]
    public async Task HistoricalPathsCannotBeReportedAsTargetDefects(string authority)
    {
        var result = await Inspect("new", "m1/us/us-regional.xml", null, "history-" + authority);
        Assert.Equal(CheckStatus.Pass, Check(result, "FDA47-1051").Status);
        Assert.Equal(CheckStatus.Pass, Check(result, "FDA47-1068").Status);
    }

    private static RuleCheckResult Check(PackageDeliveryInspection result, string rule) => Assert.Single(result.Checks, item => item.RuleId == rule);
    internal static async Task<PackageDeliveryInspection> Inspect(string? operation, string? href, string? modified, string variant = "valid", string? date = "2026-11-17",
        string? contentModule = null, string? capturedName = null, string? regionalContentHref = null, byte[]? capturedBytes = null)
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
                if (variant.StartsWith("skipped-regional", StringComparison.Ordinal) && file.LogicalPath == "0001/m1/us/us-regional.xml")
                {
                    var alternate = Path.Combine(Path.GetDirectoryName(path)!, "alternate.xml");
                    using (var original = memory.OpenRead(file.LogicalPath))
                    using (var outputFile = File.Create(alternate)) original.CopyTo(outputFile);
                    if (variant != "skipped-regional-with-default") continue;
                }
                using var input = memory.OpenRead(file.LogicalPath); using var output = File.Create(path); input.CopyTo(output);
            }
            var index = Path.Combine(source, "0001/index.xml");
            var xml = XDocument.Load(index, LoadOptions.PreserveWhitespace);
            var leaf = xml.Descendants("leaf").Single();
            leaf.SetAttributeValue("operation", operation);
            leaf.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), href);
            leaf.SetAttributeValue("modified-file", modified);
            if (contentModule is not null)
            {
                leaf.Parent!.Name = contentModule;
                leaf.SetAttributeValue("checksum", new string('0', 32));
                leaf.SetAttributeValue("checksum-type", "md5");
            }
            if (capturedName is not null)
            {
                var contentPath = Path.Combine(source, "0001", capturedName);
                Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
                if (capturedBytes is null) await File.WriteAllTextAsync(contentPath, "captured content");
                else await File.WriteAllBytesAsync(contentPath, capturedBytes);
            }
            if (variant == "namespace") leaf.SetAttributeValue(XName.Get("href", "urn:unrelated"), "m2/file.pdf");
            if (variant == "modified-namespace") leaf.SetAttributeValue(XName.Get("modified-file", "urn:unrelated"), "index.xml#regional");
            if (variant == "alias") leaf.SetAttributeValue(XNamespace.Xmlns + "link", "http://www.w3c.org/1999/xlink");
            if (variant == "skipped-regional-known-failure")
                leaf.AddAfterSelf(new XElement("leaf", new XAttribute("ID", "known-missing"), new XAttribute("operation", "new"), new XElement("title", "Missing")));
            xml.Save(index);
            if (variant == "regional" || regionalContentHref is not null)
            {
                var path = Path.Combine(source, "0001/m1/us/us-regional.xml");
                var regional = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                regional.Descendants("form").First().Add(new XElement("leaf", new XAttribute("ID", "form-leaf"), new XAttribute("operation", "new"),
                    regionalContentHref is null ? new XAttribute("modified-file", "../0000/us-regional.xml#old") :
                        new XAttribute((variant == "wrong-regional-href-namespace" ? XNamespace.Get("http://www.w3.org/1999/xlink") : regional.Root!.GetNamespaceOfPrefix("xlink")!) + "href", regionalContentHref), new XElement("title", "Form")));
                regional.Save(path);
            }
            if (variant == "truncated") await File.WriteAllTextAsync(index, "<ectd>");
            if (variant == "budget") await File.WriteAllTextAsync(Path.Combine(source, "0001/bad~name.txt"), "earlier official filename finding");
            if (variant.StartsWith("history-", StringComparison.Ordinal))
            {
                var previous = new PackageRegionalInspectionTests.MemoryInput(app, "0000", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
                foreach (var file in previous.Manifest.Files)
                {
                    var path = Path.Combine(source, file.LogicalPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var input = previous.OpenRead(file.LogicalPath); using var output = File.Create(path); input.CopyTo(output);
                }
                var previousIndex = Path.Combine(source, "0000/index.xml");
                var previousXml = XDocument.Load(previousIndex, LoadOptions.PreserveWhitespace);
                var previousLeaf = previousXml.Descendants("leaf").Single();
                previousLeaf.SetAttributeValue("operation", variant == "history-1051" ? "delete" : "new");
                if (variant == "history-1068") previousLeaf.SetAttributeValue("modified-file", "index.xml#old");
                if (variant == "history-1136") previousLeaf.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), null);
                if (variant == "history-1153-duplicate") previousLeaf.AddAfterSelf(new XElement(previousLeaf));
                if (variant == "history-1153-nonleaf") previousLeaf.Parent!.SetAttributeValue("ID", "section");
                if (variant == "history-1153-xml-id") previousLeaf.SetAttributeValue(XNamespace.Xml + "id", "alias");
                if (variant == "history-1170") previousLeaf.SetAttributeValue("operation", "replace");
                previousXml.Save(previousIndex);
                if (variant == "history-1153-incomplete") await File.AppendAllTextAsync(previousIndex, "<broken");
            }
            var snapshot = await Snapshot.Value;
            if (variant is "missing-source" or "missing-format-source" or "missing-qa-source")
            {
                var sha = variant switch
                {
                    "missing-format-source" => "8d5b4f2ef220c16324d83e44cc50595e087ccd44d1db42ebcca4084976abae6b",
                    "missing-qa-source" => "6c2fc32f3abea2d51c7a82e8d1f76b3228672dfd0968f1b9f912d43bb2454fdb",
                    _ => "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c"
                };
                snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != sha).ToArray() },
                    snapshot.Resources.Where(item => item.Sha256 != sha), snapshot.AcquiredUtc);
            }
            var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.ParseExact(date, "yyyy-MM-dd"));
            if (variant == "old-catalog")
            {
                var catalog = context.Catalog;
                context = new(new ValidationRuleCatalog(catalog.Version, catalog.Digest, catalog.Sources.Values, catalog.Profiles.Values,
                    catalog.Rules.Values.Where(rule => rule.InternalRuleId is not ("FDA47-1051" or "FDA47-1068" or "FDA47-1136" or "FDA47-1153" or "FDA47-1170"))), context.Assets, context.NodeSchema, context.SnapshotId, selectionDate: context.SelectionDate);
            }
            var limits = new PackageReadLimits { MaxBackbones = variant is "unread" or "known-failure-unread" ? 1 : 100, MaxXmlFindings = variant == "budget" ? 2 : 10_000 };
            await using var capture = await new PackageInputReader(Path.Combine(root, "validation/captures"))
                .ReadAsync(new(app, "extraneous-path-input", Path.Combine(source, "0001"), "0001"), limits);
            await using var history = variant.StartsWith("history-", StringComparison.Ordinal) ? await new PackageInputReader(Path.Combine(root, "validation/captures"))
                .ReadAsync(new(app, "history-input", Path.Combine(source, "0000"), "0000"), limits) : null;
            var baseline = new HistoryBaselineManifest(app, "0001", history is null ? [] :
                [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]);
            return await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: context)
                .InspectAsync(new(new(capture, baseline, history is null ? [] : [history])), variant == "ich-profile" ? PackageValidationCatalog.IchProfile : PackageValidationCatalog.UsProfile);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }
}
