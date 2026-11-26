using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfHistoricalContentProcessingTests
{
    private const string RuleId = "FDA47-3102";
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 26)));

    [Theory]
    [InlineData("broken", CheckStatus.Fail)]
    [InlineData("valid", CheckStatus.Pass)]
    [InlineData("unreferenced", CheckStatus.NotApplicable)]
    [InlineData("history-missing", CheckStatus.NotEvaluated)]
    [InlineData("file-missing", CheckStatus.NotEvaluated)]
    [InlineData("encoded", CheckStatus.Fail)]
    [InlineData("fragment", CheckStatus.Fail)]
    [InlineData("query", CheckStatus.NotEvaluated)]
    [InlineData("two-owners", CheckStatus.Fail)]
    public async Task CurrentLeafSelectsOnlyItsExplicitHistoricalPdf(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var result = await new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
            Assert.Equal("historical-pdf", finding.Location.LeafId);
            Assert.Contains(finding.Location.Files!, file => file.SourceId == fixture.Input.Packages.Target.SourceId && file.LogicalPath == "0001/index.xml");
            Assert.Contains(finding.Location.Files!, file => file.SourceId == fixture.Input.Packages.Sequences["0000"].SourceId && file.LogicalPath == "0000/report.pdf");
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("hash", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-document", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-reference", CheckStatus.NotEvaluated)]
    [InlineData("wrong-resolved", CheckStatus.NotEvaluated)]
    [InlineData("wrong-location", CheckStatus.NotEvaluated)]
    [InlineData("wrong-sequence", CheckStatus.NotEvaluated)]
    [InlineData("missing-node", CheckStatus.NotEvaluated)]
    [InlineData("missing-leaf", CheckStatus.NotEvaluated)]
    [InlineData("not-exists", CheckStatus.NotEvaluated)]
    [InlineData("missing-source-file", CheckStatus.NotEvaluated)]
    [InlineData("non-leaf-kind", CheckStatus.NotApplicable)]
    [InlineData("unknown-facts", CheckStatus.NotEvaluated)]
    public async Task HistoricalEvidenceRequiresExactReferenceAndCapturedHash(string variant, CheckStatus expected)
    {
        var fixture = await Fixture("broken");
        var original = Assert.Single(fixture.Files.References, item => item.Location.LeafId == "historical-pdf");
        var changed = variant switch
        {
            "wrong-resolved" => original with { Resolved = original.Resolved! with { LogicalPath = "0000/other.pdf" } },
            "wrong-location" => original with { Location = original.Location with { LogicalPath = "0001/other.xml" } },
            "wrong-sequence" => original with { Location = original.Location with { SequenceNumber = "0000" } },
            "missing-node" => original with { Location = original.Location with { NodePath = null } },
            "missing-leaf" => original with { Location = original.Location with { LeafId = null } },
            "not-exists" => original with { Exists = false },
            "missing-source-file" => original with { SourceLogicalPath = "0001/absent.xml", Location = original.Location with { LogicalPath = "0001/absent.xml" } },
            "non-leaf-kind" => original with { Kind = "Stylesheet" },
            _ => original
        };
        var references = fixture.Files.References.Select(item => item == original ? changed : item);
        if (variant == "duplicate-reference") references = references.Append(changed);
        var files = new PackageFileInspection(fixture.Input, fixture.Lifecycle, references, fixture.Files.Checks);
        var document = Evidence(fixture);
        if (variant == "hash") document = document with { CapturedSha256 = new string('f', 64) };
        if (variant == "unknown-facts") document = document with { Facts = null };
        var check = FdaPdfContentProcessingRule.Inspect(fixture.Input, files, variant == "duplicate-document" ? [document, document] : [document],
            fixture.Context, 10, CancellationToken.None)!;
        Assert.Equal(expected, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(PdfContentProcessingStatus.InvalidContent, CheckStatus.Fail)]
    [InlineData(PdfContentProcessingStatus.Encrypted, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.ResourceLimited, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.InputUnavailable, CheckStatus.NotEvaluated)]
    [InlineData(PdfContentProcessingStatus.Unknown, CheckStatus.NotEvaluated)]
    public async Task HistoricalProcessingCategoriesAreNotInferredFromMessages(PdfContentProcessingStatus status, CheckStatus expected)
    {
        var fixture = await Fixture("broken");
        var document = Evidence(fixture);
        var facts = document.Facts!;
        document = document with { Facts = new(facts.Basics, [], [], false, false, contentProcessing: status) };
        var check = FdaPdfContentProcessingRule.Inspect(fixture.Input, fixture.Files, [document], fixture.Context, 10, CancellationToken.None)!;
        Assert.Equal(expected, check.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task HistoricalFindingUsesTheSharedAllowance(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture("two-owners");
        var check = FdaPdfContentProcessingRule.Inspect(fixture.Input, fixture.Files, [Evidence(fixture)], fixture.Context, remaining, CancellationToken.None)!;
        Assert.Equal(expected, check.Status);
        Assert.True(check.Findings.Count <= remaining);
    }

    [Fact]
    public async Task PartialReportDoesNotPromoteHistoricalProcessingToCompleteCoverage()
    {
        var fixture = await Fixture("valid");
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Pdf.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Fact]
    public async Task HistoricalFailureProgressSurvivesCancellation()
    {
        var fixture = await Fixture("broken");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaPdfContentProcessingRule.Inspect(fixture.Input, fixture.Files, [Evidence(fixture)], fixture.Context,
            10, cancellation.Token, result => { retained = result; if (result.Status == CheckStatus.Fail) cancellation.Cancel(); }));
        Assert.Equal(CheckStatus.Fail, retained!.Status);
        Assert.Single(retained.Findings);
    }

    private static PackagePdfDocument Evidence(FixtureData fixture)
    {
        var capture = fixture.Input.Packages.Sequences["0000"];
        var file = Assert.Single(capture.Manifest.Files, item => item.LogicalPath == "0000/report.pdf");
        using var stream = capture.OpenRead(file.LogicalPath);
        return new(file.LogicalPath, new PdfPigPackageInspector().Inspect(stream, file.LogicalPath, capture.Limits, CancellationToken.None), file.Sha256);
    }

    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context, PackageLifecycleInspection Lifecycle);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var context = await Context.Value;
        var app = Guid.NewGuid();
        var target = new Capture(app, "0001", scenario);
        var history = scenario == "history-missing" ? null : new Capture(app, "0000", scenario);
        var packages = new PackageInputSet(target, new(app, "0001", history is null ? [] :
            [new("0000", HistorySourceKind.ImportedExternal, history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]),
            history is null ? [] : [history]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        Assert.All(lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), document => Assert.Equal(CheckStatus.Pass, document.DtdStatus));
        var input = new PackageDeliveryInput(packages);
        var files = await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle);
        if (scenario != "unreferenced")
        {
            var reference = Assert.Single(files.References, item => item.SourceLogicalPath == "0001/index.xml" && item.Location.LeafId == "historical-pdf");
            Assert.Equal("0000/report.pdf", reference.Resolved?.LogicalPath);
            Assert.Equal(history is not null && scenario != "file-missing", reference.Exists);
        }
        return new(input, files, context, lifecycle);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(Guid app, string sequence, string scenario)
        {
            var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            regional.Descendants("sequence-number").Single().Value = sequence;
            var original = new PackageRegionalInspectionTests.MemoryInput(app, sequence, regional.ToString(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.Where(file => !file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToDictionary(file => file.LogicalPath,
                file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            var index = XDocument.Parse(Encoding.UTF8.GetString(_files[sequence + "/index.xml"]));
            var regionalLeaf = index.Descendants("leaf").Single();
            regionalLeaf.SetAttributeValue("checksum", new string('0', 32));
            regionalLeaf.SetAttributeValue("checksum-type", "md5");
            if (sequence == "0000" && scenario != "file-missing") _files["0000/report.pdf"] = scenario == "valid" ? PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]") : "not a pdf"u8.ToArray();
            if (sequence == "0001" && scenario != "unreferenced")
            {
                index.Root!.Add(new XElement("m2-common-technical-document-summaries", new XElement("m2-5-clinical-overview",
                    new XElement("leaf", new XAttribute("ID", "historical-pdf"), new XAttribute("operation", "new"),
                        new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
                        new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), scenario switch
                        {
                            "encoded" => "../0000/re%70ort.pdf", "fragment" => "../0000/report.pdf#page=1",
                            "query" => "../0000/report.pdf?version=1", _ => "../0000/report.pdf"
                        }), new XElement("title", "Historical report")))));
                if (scenario == "two-owners")
                {
                    var first = index.Descendants("leaf").Single(item => (string?)item.Attribute("ID") == "historical-pdf");
                    var second = new XElement(first); second.SetAttributeValue("ID", "historical-pdf-2"); first.AddAfterSelf(second);
                }
            }
            _files[sequence + "/index.xml"] = Encoding.UTF8.GetBytes(index.ToString());
            Manifest = new(app, sequence, _files.Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda3102-history-" + Manifest.SequenceNumber;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
