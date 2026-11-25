using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfAnnotationRuleTests
{
    private const string RuleId = "FDA47-5055";

    [Theory]
    [InlineData("cover", CheckStatus.Fail)]
    [InlineData("promotional", CheckStatus.NotEvaluated)]
    [InlineData("both", CheckStatus.Fail)]
    [InlineData("folder-name", CheckStatus.Fail)]
    [InlineData("extension", CheckStatus.Fail)]
    [InlineData("promotional-extension", CheckStatus.NotEvaluated)]
    [InlineData("unreferenced", CheckStatus.NotEvaluated)]
    [InlineData("delete", CheckStatus.NotEvaluated)]
    [InlineData("history", CheckStatus.NotEvaluated)]
    [InlineData("missing", CheckStatus.NotEvaluated)]
    [InlineData("broken-tail", CheckStatus.NotEvaluated)]
    [InlineData("same-file", CheckStatus.Fail)]
    public async Task ActualRegionalAncestryControlsException(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var delivery = await Run(fixture);
        if (scenario != "broken-tail")
            Assert.Equal(CheckStatus.Pass, Assert.Single(delivery.Lifecycle.XmlInspections.Single().Backbones, item => item.Kind == BackboneKind.UsRegional).DtdStatus);
        var check = Assert.Single(delivery.Pdf.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("outside", finding.Location.LeafId);
            Assert.Contains("Text", finding.Actual);
            Assert.Contains("future submissions", finding.RecommendedAction);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("Text", CheckStatus.Fail)]
    [InlineData("FreeText", CheckStatus.Fail)]
    [InlineData("Highlight", CheckStatus.Fail)]
    [InlineData("Underline", CheckStatus.Fail)]
    [InlineData("StrikeOut", CheckStatus.Fail)]
    [InlineData("Squiggly", CheckStatus.Fail)]
    [InlineData("Stamp", CheckStatus.Fail)]
    [InlineData("Ink", CheckStatus.Fail)]
    [InlineData("Line", CheckStatus.Fail)]
    [InlineData("Square", CheckStatus.Fail)]
    [InlineData("Circle", CheckStatus.Fail)]
    [InlineData("Polygon", CheckStatus.Fail)]
    [InlineData("PolyLine", CheckStatus.Fail)]
    [InlineData("Caret", CheckStatus.Fail)]
    [InlineData("Link", CheckStatus.NotEvaluated)]
    [InlineData("Widget", CheckStatus.NotEvaluated)]
    [InlineData("UnknownSubtype", CheckStatus.NotEvaluated)]
    [InlineData("none", CheckStatus.NotEvaluated)]
    public async Task MarkupIsNotInferredFromAnyAnnotsEntry(string subtype, CheckStatus expected)
    {
        var result = await Run(await Fixture("cover", subtype));
        Assert.Null(Assert.Single(result.Pdf.Documents).Facts!.Basics.ParseError);
        Assert.Equal(expected, Assert.Single(result.Pdf.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("2016-09-13", CheckStatus.NotApplicable)]
    [InlineData("2016-09-14", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
    {
        var result = await Run(await Fixture("cover", date: date));
        Assert.Equal(expected, Assert.Single(result.Pdf.Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task MissingArchivedCriteriaDoesNotAuthorizeFailure()
    {
        var result = await Run(await Fixture("cover", missingSource: true));
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Pdf.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("<< /Type /Annot /Subtype /Text /Rect [0 0 100 100] >>", 1)]
    [InlineData("<< /Subtype /Highlight /Rect [0 0 100 100] /P 3 0 R >>", 1)]
    [InlineData("<< /Subtype /Text /Rect [0 0 100 100] /P 2 0 R >>", 0)]
    [InlineData("<< /Subtype /Text /Rect [0 0 100 100] /P null >>", 0)]
    [InlineData("<< /Type /Other /Subtype /Text /Rect [0 0 100 100] >>", 0)]
    [InlineData("<< /Type /Annot /Subtype /Link /Rect [0 0 100 100] /Dest [3 0 R /Fit] >>", 0)]
    [InlineData("<< /Type /Annot /Subtype /Widget /Rect [0 0 100 100] >>", 0)]
    public void AdapterRequiresIdentifiedMarkupAndConsistentPageOwner(string annotation, int count)
    {
        using var stream = new MemoryStream(Build("Text", annotation));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        Assert.Equal(count, facts.MarkupAnnotations.Count);
        if (count > 0) { Assert.Equal(1, facts.MarkupAnnotations[0].PageNumber); Assert.Equal("page[1]/annotation[1]", facts.MarkupAnnotations[0].ObjectPath); }
    }

    [Theory]
    [InlineData("<< /Subtype /Text /Rect [0 0 100] >>")]
    [InlineData("<< /Subtype /Text /Rect [0 0 (100) 100] >>")]
    [InlineData("<< /Subtype /Text >>")]
    [InlineData("<< /Subtype (Text) /Rect [0 0 100 100] >>")]
    public async Task BaseParserRejectedAnnotationsDoNotSupplyQualifiedMarkup(string annotation)
    {
        var result = await Run(await Fixture("cover", annotation: annotation));
        var facts = Assert.Single(result.Pdf.Documents).Facts!;
        Assert.NotNull(facts.Basics.ParseError);
        Assert.NotEqual(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        Assert.Empty(facts.MarkupAnnotations);
        var check = Assert.Single(result.Pdf.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task LegacyPdfCallerWithoutLifecycleCannotInferLocation()
    {
        var fixture = await Fixture("cover"); var delivery = await Run(fixture);
        var result = await new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, delivery.Files);
        Assert.NotEmpty(Assert.Single(result.Documents).Facts!.MarkupAnnotations);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("page")]
    [InlineData("duplicate")]
    [InlineData("unknown-type")]
    [InlineData("missing-facts")]
    public async Task UnqualifiedPdfFactsCannotAuthorizeAnnotationFailure(string scenario)
    {
        var fixture = await Fixture("cover"); var delivery = await Run(fixture);
        var document = Assert.Single(delivery.Pdf.Documents);
        var annotation = new PdfMarkupAnnotation("page[1]/annotation[1]", scenario == "page" ? 2 : 1, scenario == "unknown-type" ? "Link" : "Text");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], false, [], PageCount: 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            markupAnnotations: scenario == "duplicate" ? [annotation, annotation] : [annotation]);
        var run = new FdaPdfAnnotationRule.Inspection(fixture.Input, delivery.Files, delivery.Lifecycle, fixture.Context, CancellationToken.None);
        run.Observe(document with { Facts = scenario == "missing-facts" ? null : facts, CapturedSha256 = scenario == "hash" ? new string('0', 64) : document.CapturedSha256 }, 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rules")]
    [InlineData("assets")]
    [InlineData("duplicate-xml")]
    [InlineData("duplicate-regional")]
    [InlineData("missing-dtd")]
    [InlineData("incomplete")]
    [InlineData("bad-children")]
    [InlineData("bad-namespace")]
    [InlineData("leaf-href")]
    [InlineData("duplicate-leaf")]
    public async Task UnqualifiedFrozenLocationCannotAuthorizeFailure(string scenario)
    {
        var fixture = await Fixture("cover"); var delivery = await Run(fixture);
        var xml = Assert.Single(delivery.Lifecycle.XmlInspections);
        var regional = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        var elements = regional.Elements.Select(element => scenario is "bad-children" or "bad-namespace" && element.ParentPath is null
            ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName,
                scenario == "bad-namespace" ? "urn:wrong" : element.NamespaceUri, element.Attributes.Values,
                scenario == "bad-children" ? [] : element.Children, element.Text, element.Location) : element).ToArray();
        var leaves = scenario == "duplicate-leaf" ? regional.Leaves.Concat(regional.Leaves.Take(1)).ToArray() : regional.Leaves
            .Select(leaf => scenario == "leaf-href" ? leaf with { Href = "different.pdf" } : leaf).ToArray();
        var changed = new ParsedBackbone(regional.LogicalPath, regional.ProfileSnapshotId, regional.Kind, scenario != "incomplete", regional.DtdStatus,
            regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier, elements, regional.Nodes, leaves,
            regional.IdIndex.Values.SelectMany(value => value), scenario == "missing-dtd" ? [] : regional.ResolvedAssets);
        var documents = xml.Backbones.Select(item => item == regional ? changed : item).ToList();
        if (scenario == "duplicate-regional") documents.Add(changed);
        var modified = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, scenario == "assets" ? new string('0', 64) : xml.AssetManifestDigest,
            xml.LimitsDigest, documents, xml.Checks, scenario == "rules" ? new string('0', 64) : xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Input.Packages, delivery.Lifecycle.ProfileSnapshotId,
            scenario == "duplicate-xml" ? [modified, modified] : [modified], [], [], [], fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var run = new FdaPdfAnnotationRule.Inspection(fixture.Input, delivery.Files, scenario == "missing" ? null : lifecycle, fixture.Context, CancellationToken.None);
        run.Observe(Assert.Single(delivery.Pdf.Documents), 10);
        Assert.Equal(CheckStatus.NotEvaluated, run.Finish(false)!.Status);
        Assert.Equal(0, run.FindingCount);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExactFindingAllowanceAndInterruptedFinish(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("cover"); var delivery = await Run(fixture);
        var run = new FdaPdfAnnotationRule.Inspection(fixture.Input, delivery.Files, delivery.Lifecycle, fixture.Context, CancellationToken.None);
        run.Observe(Assert.Single(delivery.Pdf.Documents), remaining);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture("cover"); var delivery = await Run(fixture);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfAnnotationRule.Inspection(fixture.Input, delivery.Files, delivery.Lifecycle, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(Assert.Single(delivery.Pdf.Documents), 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("cover", CheckStatus.Fail)]
    [InlineData("promotional", CheckStatus.NotEvaluated)]
    public async Task PartialReportPreservesExceptionBoundary(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario); var delivery = await Run(fixture);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task SharedPdfBudgetIncludesAnnotationFinding(int limit)
    {
        var fixture = await Fixture("cover", limits: new() { MaxXmlFindings = limit });
        var delivery = await Run(fixture);
        Assert.Equal(CheckStatus.Fail, Assert.Single(delivery.Pdf.Checks, item => item.RuleId == RuleId).Status);
        Assert.True(delivery.Pdf.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(delivery.Pdf.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("cover"); var delivery = await Run(fixture);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new FdaPdfAnnotationRule.Inspection(fixture.Input, delivery.Files, delivery.Lifecycle, fixture.Context, cancellation.Token));
    }

    [Fact]
    public async Task KnownAnnotationSurvivesLaterDocumentLimit()
    {
        var fixture = await Fixture("cover", secondPdf: true); var delivery = await Run(fixture);
        var result = await new PackagePdfValidator(new LaterLimitInspector(), standards: fixture.Context, lifecycle: delivery.Lifecycle).InspectAsync(fixture.Input, delivery.Files);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    private sealed class LaterLimitInspector : IPackagePdfInspector
    {
        private int _count;
        public string EngineVersion => "later-limit-annotation-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
        {
            if (_count++ > 0) throw new PdfInspectionLimitException("Later document limit.");
            return new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
        }
    }

    private static Task<PackageDeliveryInspection> Run(FixtureData fixture) =>
        new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string subtype = "Text", string? date = "2026-11-25", bool missingSource = false, PackageReadLimits? limits = null,
        string? annotation = null, bool secondPdf = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingSource)
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var capture = new Capture(scenario, subtype, limits ?? new(), annotation, secondPdf);
        return new(new(new(capture, new(capture.Manifest.ApplicationId, "0001", []), [])), context);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(string scenario, string subtype, PackageReadLimits limits, string? annotation, bool secondPdf)
        {
            Limits = limits; var app = Guid.NewGuid();
            var pdf = Build(subtype, annotation);
            var xml = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            var module = xml.Descendants("m1-regional").Single();
            var path = scenario == "folder-name" ? "m1-15-promotional-material/report.pdf" : "report.pdf";
            var href = scenario == "history" ? "../../../0000/m1/us/report.pdf" : path;
            var leaf = Leaf("outside", href, scenario == "delete" ? "delete" : "new", pdf);
            if (scenario is not ("promotional" or "promotional-extension" or "unreferenced"))
            {
                var cover = new XElement("m1-2-cover-letters", scenario == "extension" ? Extension(leaf) : leaf);
                if (scenario == "same-file") cover.Add(Leaf("alias", path + "#page=1", "new", pdf));
                module.Add(cover);
            }
            if (scenario is "promotional" or "promotional-extension" or "both")
            {
                var allowed = Leaf("inside", path, "new", pdf);
                module.Add(new XElement("m1-15-promotional-material", new XAttribute("promotional-material-audience-type", "fdapmat1"),
                    new XElement("m1-15-1-correspondence-relating-to-promotional-materials",
                        new XElement("m1-15-1-10-submission-of-annotated-references", scenario == "promotional-extension" ? Extension(allowed) : allowed))));
            }
            var text = xml.ToString(SaveOptions.DisableFormatting);
            if (scenario == "broken-tail") text += "<broken";
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", text, PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (scenario != "missing") _files["0001/m1/us/" + path] = pdf;
            if (secondPdf) _files["0001/z-other.pdf"] = pdf;
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        private static XElement Extension(XElement leaf) => new("node-extension", new XAttribute("ID", "extension"), new XElement("title", "Custom"), leaf);
        private static XElement Leaf(string id, string href, string operation, byte[] bytes) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", operation),
            new XAttribute("checksum-type", "md5"), new XAttribute("checksum", Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant()),
            new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XElement("title", id));
        public string SourceId => "fda5055";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static byte[] Build(string subtype, string? annotation = null)
    {
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> " + (subtype == "none" ? "" : "/Annots [4 0 R]") + " >>",
            annotation ?? "<< /Type /Annot /Subtype /" + subtype + " /Rect [0 0 100 100] /Contents (Test note) /Dest [3 0 R /Fit] >>"
        };
        var text = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++) { offsets.Add(Encoding.ASCII.GetByteCount(text.ToString())); text.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(text.ToString()); text.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        text.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
