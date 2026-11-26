using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPdfLinearizationRuleTests
{
    private const string RuleId = "FDA47-5040";

    [Theory]
    [InlineData("42")]
    [InlineData("(ordinary)")]
    [InlineData("[1 2 3]")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("/Linearized")]
    [InlineData("<4C696E656172697A6564>")]
    [InlineData("[]")]
    [InlineData("[<< /Linearized 1 >>]")]
    [InlineData("1.25")]
    [InlineData("-42")]
    public async Task CompleteNonDictionaryFirstObjectCannotBeLinearized(string first)
    {
        var result = await Run(await Fixture(PhysicalPdf(first, "")));
        var facts = Assert.Single(result.Documents).Facts!;
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        Assert.Equal(PdfLinearizationStatus.NonDictionaryFirstObject, facts.Linearization);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("Non-dictionary first object", Assert.Single(check.Findings).Actual);
    }

    [Theory]
    [InlineData(1023, PdfLinearizationStatus.NonDictionaryFirstObject)]
    [InlineData(1024, PdfLinearizationStatus.Unknown)]
    [InlineData(1025, PdfLinearizationStatus.Unknown)]
    public void NonDictionaryEndRequiresDelimiterWithinWindow(int delimiterOffset, PdfLinearizationStatus expected)
    {
        var plain = PhysicalPdf("42", "");
        var end = Encoding.ASCII.GetString(plain).IndexOf("endobj", StringComparison.Ordinal) + 6;
        var bytes = PhysicalPdf("42", "%" + new string('x', delimiterOffset - end - 2) + "\n");
        Assert.Equal(delimiterOffset, Encoding.ASCII.GetString(bytes).IndexOf("endobj", StringComparison.Ordinal) + 6);
        using var stream = new MemoryStream(bytes); stream.Position = 7;
        Assert.Equal(expected, PdfPigPackageLinearizationReader.Read(stream, new(), CancellationToken.None));
        Assert.Equal(7, stream.Position);
    }

    [Theory]
    [InlineData("%PDF-1.4\n1 0 obj [1 2 endobj\n")]
    [InlineData("%PDF-1.4\n1 0 obj (unfinished endobj\n")]
    [InlineData("%PDF-1.4\n1 0 obj 42 endobj")]
    [InlineData("%PDF-1.4\n1 0 obj 42 endobject\n")]
    [InlineData("%PDF-1.4\n1 0 obj 42 43 endobj\n")]
    [InlineData("%PDF-1.4\n1 0 obj 2 0 R endobj\n")]
    [InlineData("%PDF-1.4\n1 0 obj unknown endobj\n")]
    [InlineData("%PDF-1.4\n1 0 obj << /Length 0 >> stream\nendstream endobj\n")]
    public void IncompleteOrUnsupportedFirstObjectsRemainUnknown(string source)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(source));
        Assert.Equal(PdfLinearizationStatus.Unknown, PdfPigPackageLinearizationReader.Read(stream, new(), CancellationToken.None));
    }

    [Fact]
    public void NonDictionaryReadHonorsWorkLimitAndCancellation()
    {
        using var stream = new MemoryStream(PhysicalPdf("42", ""));
        Assert.Equal(PdfLinearizationStatus.Unknown, PdfPigPackageLinearizationReader.Read(stream, new() { MaxPdfObjects = 1 }, CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PdfPigPackageLinearizationReader.Read(stream, new(), cancellation.Token));
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task NonDictionaryFailureUsesSharedAllowance(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(PhysicalPdf("42", ""));
        var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfLinearizationRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining);
        Assert.Equal(expected, run.Finish(true)!.Status);
    }

    [Theory]
    [InlineData("plain", CheckStatus.Fail)]
    [InlineData("declared", CheckStatus.NotEvaluated)]
    [InlineData("null", CheckStatus.NotEvaluated)]
    [InlineData("escaped", CheckStatus.NotEvaluated)]
    [InlineData("string", CheckStatus.Fail)]
    [InlineData("nested", CheckStatus.Fail)]
    [InlineData("late-object", CheckStatus.Fail)]
    [InlineData("large-first", CheckStatus.NotEvaluated)]
    public async Task PhysicalFirstObjectIsNotAStringSearch(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(Build(scenario));
        var result = await Run(fixture);
        Assert.Null(Assert.Single(result.Documents).Facts!.Basics.ParseError);
        var check = Assert.Single(result.Checks, item => item.RuleId == RuleId);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0001/report.pdf", finding.Location.LogicalPath);
            Assert.Contains("future submissions", finding.RecommendedAction);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2013-04-30", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
    {
        var fixture = await Fixture(Build("plain"), date);
        Assert.Equal(expected, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Fact]
    public async Task MissingArchivedCriteriaCannotAuthorizeFailure()
    {
        var fixture = await Fixture(Build("plain"), missingSource: true);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(false, CheckStatus.NotApplicable)]
    [InlineData(true, CheckStatus.NotEvaluated)]
    public async Task NoPdfAndUnreadablePdfAreDifferent(bool broken, CheckStatus expected)
    {
        var fixture = await Fixture(broken ? "not a pdf"u8.ToArray() : null);
        Assert.Equal(expected, Assert.Single((await Run(fixture)).Checks, item => item.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("plain", PdfLinearizationStatus.MissingFirstObjectDeclaration)]
    [InlineData("declared", PdfLinearizationStatus.DeclaredUnverified)]
    [InlineData("null", PdfLinearizationStatus.DeclaredUnverified)]
    [InlineData("escaped", PdfLinearizationStatus.DeclaredUnverified)]
    [InlineData("string", PdfLinearizationStatus.MissingFirstObjectDeclaration)]
    [InlineData("nested", PdfLinearizationStatus.MissingFirstObjectDeclaration)]
    [InlineData("large-first", PdfLinearizationStatus.Unknown)]
    public void AdapterRetainsMissingDeclaredAndUnknownStates(string scenario, PdfLinearizationStatus expected)
    {
        using var stream = new MemoryStream(Build(scenario));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(PdfContentProcessingStatus.Processed, facts.ContentProcessing);
        Assert.Equal(expected, facts.Linearization);
    }

    [Theory]
    [InlineData("<< /Linearized 1 >>", "% comment\n", PdfLinearizationStatus.DeclaredUnverified)]
    [InlineData("<< /Note (ordinary) >>", "% /Linearized 1\n", PdfLinearizationStatus.MissingFirstObjectDeclaration)]
    [InlineData("<< /Other << /Linearized 1 >> >>", "", PdfLinearizationStatus.MissingFirstObjectDeclaration)]
    [InlineData("42", "", PdfLinearizationStatus.NonDictionaryFirstObject)]
    [InlineData("<< /Length 0 >>\nstream\n\nendstream", "", PdfLinearizationStatus.Unknown)]
    public void PhysicalFirstObjectIsIndependentOfCatalogAndComments(string first, string comments, PdfLinearizationStatus expected)
    {
        using var stream = new MemoryStream(PhysicalPdf(first, comments));
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(expected, facts.Linearization);
    }

    [Theory]
    [InlineData(1023, PdfLinearizationStatus.MissingFirstObjectDeclaration)]
    [InlineData(1024, PdfLinearizationStatus.Unknown)]
    [InlineData(1025, PdfLinearizationStatus.Unknown)]
    public void EndObjectRequiresRealDelimiterInsideReadWindow(int delimiterOffset, PdfLinearizationStatus expected)
    {
        const string first = "<< /Ordinary true >>";
        var plain = PhysicalPdf(first, "");
        var end = Encoding.ASCII.GetString(plain).IndexOf("endobj", StringComparison.Ordinal) + 6;
        var comments = "%" + new string('x', delimiterOffset - end - 2) + "\n";
        var bytes = PhysicalPdf(first, comments);
        Assert.Equal(delimiterOffset, Encoding.ASCII.GetString(bytes).IndexOf("endobj", StringComparison.Ordinal) + 6);
        using var stream = new MemoryStream(bytes);
        var facts = new PdfPigPackageInspector().Inspect(stream, "0001/report.pdf", new(), CancellationToken.None);
        Assert.Null(facts.Basics.ParseError);
        Assert.Equal(expected, facts.Linearization);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("legacy")]
    [InlineData("parse")]
    [InlineData("encrypted")]
    [InlineData("declared")]
    [InlineData("unknown")]
    [InlineData("missing-facts")]
    public async Task UnqualifiedFactsCannotAuthorizeMissingDeclaration(string scenario)
    {
        var fixture = await Fixture(Build("plain"));
        var file = fixture.Input.Packages.Target.Manifest.Files.Single(item => item.LogicalPath == "0001/report.pdf");
        var basics = new PdfInspectionResult("1.4", scenario == "encrypted", false, true, true, [], false, [], PageCount: 1, ParseError: scenario == "parse" ? "opaque" : null);
        var facts = new PackagePdfFacts(basics, [], [], true, true, contentProcessing: scenario == "legacy" ? PdfContentProcessingStatus.Unknown : PdfContentProcessingStatus.Processed,
            linearization: scenario == "declared" ? PdfLinearizationStatus.DeclaredUnverified : scenario == "unknown" ? PdfLinearizationStatus.Unknown : PdfLinearizationStatus.MissingFirstObjectDeclaration);
        var run = new FdaPdfLinearizationRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(new(file.LogicalPath, scenario == "missing-facts" ? null : facts, scenario == "hash" ? new string('0', 64) : file.Sha256), 10);
        var check = run.Finish(false)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task ExactFindingAllowanceAndInterruptedFinish(int remaining, CheckStatus expected)
    {
        var fixture = await Fixture(Build("plain")); var document = Assert.Single((await Run(fixture)).Documents);
        var run = new FdaPdfLinearizationRule.Inspection(fixture.Input, fixture.Files, fixture.Context, CancellationToken.None);
        run.Observe(document, remaining);
        Assert.Equal(expected, run.Finish(true)!.Status); Assert.Equal(remaining, run.FindingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPreservesUnknownOrKnownFailure(bool afterFailure)
    {
        var fixture = await Fixture(Build("plain")); var document = Assert.Single((await Run(fixture)).Documents);
        using var cancellation = new CancellationTokenSource(); RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            var run = new FdaPdfLinearizationRule.Inspection(fixture.Input, fixture.Files, fixture.Context, cancellation.Token,
                result => { retained = result; if (!afterFailure || result.Status == CheckStatus.Fail) cancellation.Cancel(); });
            run.Observe(document, 10);
        });
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("plain", CheckStatus.Fail)]
    [InlineData("declared", CheckStatus.NotEvaluated)]
    public async Task ReportPreservesPartialBoundary(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(Build(scenario));
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(delivery.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task SharedBudgetRetainsLinearizationFailure(int limit)
    {
        var fixture = await Fixture(Build("plain"), limits: new() { MaxXmlFindings = limit });
        var result = await Run(fixture);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.True(result.Checks.Sum(item => item.Findings.Count) <= limit);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    [Fact]
    public async Task KnownFailureSurvivesLaterInspectorLimit()
    {
        var fixture = await Fixture(Build("plain"), secondPdf: true);
        var result = await new PackagePdfValidator(new LaterLimitInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, item => item.RuleId == RuleId).Status);
        Assert.Contains(result.Checks.SelectMany(item => item.Findings), item => item.Code == "PDF_INSPECTION_LIMIT");
    }

    private sealed class LaterLimitInspector : IPackagePdfInspector
    {
        private int _count;
        public string EngineVersion => "later-limit-linearization-fixture";
        public PackagePdfFacts Inspect(Stream stream, string logicalPath, PackageReadLimits limits, CancellationToken cancellationToken)
        {
            if (_count++ > 0) throw new PdfInspectionLimitException("Later document resource limit.");
            return new PdfPigPackageInspector().Inspect(stream, logicalPath, limits, cancellationToken);
        }
    }

    // The unreferenced physical first object is deliberately not the catalog.
    private static byte[] PhysicalPdf(string first, string comments)
    {
        var objects = new[] { first, "<< /Type /Catalog /Pages 3 0 R >>", "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 612 792] /Resources << >> >>" };
        var text = new StringBuilder("%PDF-1.4\n" + comments); var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(text.ToString()));
            text.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(text.ToString());
        text.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        text.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 2 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    private static byte[] Build(string scenario)
    {
        var catalog = scenario switch
        {
            "declared" => "/Linearized 1", "null" => "/Linearized null", "escaped" => "/Linear#69zed 1",
            "string" => "/Note (Contains /Linearized 1 text)", "nested" => "/Other << /Linearized 1 >>",
            "large-first" => "/Note (" + new string('x', 1300) + ")", _ => ""
        };
        return PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", catalog,
            scenario == "late-object" ? "<< /Linearized 1 >>" : "<< /Unused true >>");
    }
    private static Task<PackagePdfInspection> Run(FixtureData fixture) => new PackagePdfValidator(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, fixture.Files);
    private sealed record FixtureData(PackageDeliveryInput Input, PackageFileInspection Files, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(byte[]? pdf, string? date = "2026-11-25", bool missingSource = false, PackageReadLimits? limits = null, bool secondPdf = false)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        if (missingSource)
        {
            const string hash = "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));
        var capture = new Capture(pdf, limits ?? new(), secondPdf);
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var input = new PackageDeliveryInput(packages);
        return new(input, await new PackageFileInspector(standards: context).InspectAsync(input, lifecycle), context);
    }
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(byte[]? pdf, PackageReadLimits limits, bool secondPdf)
        {
            Limits = limits; var app = Guid.NewGuid();
            var original = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            if (pdf is not null) _files["0001/report.pdf"] = pdf;
            if (pdf is not null && secondPdf) _files["0001/z-other.pdf"] = pdf;
            Manifest = new(app, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5040";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
