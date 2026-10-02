using System.Text;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageDeliveryInspectorTests
{
    [Fact]
    public async Task CompleteDeliveryPipelineBindsBothArtifactsAndPreservesCoverageGaps()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        fixture.CreateZip("0003", "same");
        var (input, _) = await fixture.SelectAsync("0003", comparison: true);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector()).InspectAsync(input, PackageValidationCatalog.UsProfile);
        Assert.DoesNotContain(result.Checks.SelectMany(check => check.Findings), finding => finding.CheckStatus == CheckStatus.Fail);
        Assert.Equal(CheckStatus.Pass, result.Checks.Single(check => check.RuleId == "ZIP-PARITY").Status);
        Assert.Equal(CheckStatus.Pass, result.Checks.Single(check => check.RuleId == "FILE-MD5").Status);
        Assert.Equal(CheckStatus.Pass, result.Checks.Single(check => check.RuleId == "PDF-READABLE").Status);
        Assert.Equal(13, result.Pdf.Documents.Count);
        var report = result.CreateReport(PackageValidationMode.Formal);
        Assert.True(report.ExecutionCompleted);
        Assert.False(report.IsReadyForFinalization);
        Assert.Equal(input.InputDigest, report.Binding.InputManifestDigest);
        Assert.Contains("pdfpig-0.1.15", report.Binding.EngineVersion, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == "UNREFERENCED_DELIVERY_FILE");
    }

    [Fact]
    public async Task ADisconnectedPdfCycleCannotMakeItselfReachableFromTheBackbone()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        await File.WriteAllBytesAsync(fixture.PathOf("0000/orphan-a.pdf"), PdfPigPackageInspectorTests.Build("/A << /S /GoToR /F (orphan-b.pdf) /D [0 /Fit] >>"));
        await File.WriteAllBytesAsync(fixture.PathOf("0000/orphan-b.pdf"), PdfPigPackageInspectorTests.Build("/A << /S /GoToR /F (orphan-a.pdf) /D [0 /Fit] >>"));
        var (input, _) = await fixture.SelectAsync("0000");
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector()).InspectAsync(input, PackageValidationCatalog.UsProfile);
        var structure = result.Checks.Single(check => check.RuleId == "DELIVERY-STRUCTURE");
        Assert.Equal(CheckStatus.NotEvaluated, structure.Status);
        Assert.Equal(2, structure.Findings.Count(finding => finding.Code == "UNREFERENCED_DELIVERY_FILE"));
    }

    [Theory]
    [InlineData("missing", "STYLESHEET_MISSING")]
    [InlineData("changed", "STYLESHEET_DIGEST_MISMATCH")]
    [InlineData("external", "STYLESHEET_OUT_OF_SCOPE")]
    [InlineData("unknown", "STYLESHEET_NOT_PINNED")]
    [InlineData("entity", "STYLESHEET_XML_INVALID")]
    [InlineData("include", "STYLESHEET_MISSING")]
    public async Task StylesheetReferencesAreInspectedWithoutExecutingOrFetchingThem(string variant, string code)
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var href = variant == "external" ? "https://example.invalid/style.xsl" : variant is "unknown" or "entity" or "include" ? "util/style/custom.xsl" : "util/style/ectd-2-0.xsl";
        fixture.Replace("0000/index.xml", "<!DOCTYPE", "<?xml-stylesheet type=\"text/xsl\" href=\"" + href + "\"?>\n<!DOCTYPE");
        var directory = fixture.PathOf("0000/util/style");
        Directory.CreateDirectory(directory);
        if (variant == "changed")
        {
            File.Copy(Path.Combine(PackageRuleCatalogTests.Root, "reference/eu-m1/3.1.1/util/style/ectd-2-0.xsl"), Path.Combine(directory, "ectd-2-0.xsl"));
            File.AppendAllText(Path.Combine(directory, "ectd-2-0.xsl"), "\n<!-- changed -->");
        }
        if (variant is "unknown" or "entity" or "include")
            File.WriteAllText(Path.Combine(directory, "custom.xsl"), (variant == "entity" ? "<!DOCTYPE xsl:stylesheet [<!ENTITY external SYSTEM 'file:///unread'>]>" : "") +
                "<xsl:stylesheet xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" version=\"1.0\">" +
                (variant == "include" ? "<xsl:include href=\"missing.xsl\"/>" : "") + "</xsl:stylesheet>", new UTF8Encoding(false));
        var (input, lifecycle) = await fixture.SelectAsync("0000");
        var result = await new PackageFileInspector().InspectAsync(input, lifecycle);
        Assert.Contains(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == code);
        Assert.Single(lifecycle.XmlInspections[0].Backbones[0].ProcessingInstructions, instruction => instruction.Target == "xml-stylesheet");
    }

    [Fact]
    public async Task PinnedStylesheetBytesPassWithoutBeingAllowedAsADtdResource()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        fixture.Replace("0000/index.xml", "<!DOCTYPE", "<?xml-stylesheet type=\"text/xsl\" href=\"util/style/ectd-2-0.xsl\"?>\n<!DOCTYPE");
        Directory.CreateDirectory(fixture.PathOf("0000/util/style"));
        File.Copy(Path.Combine(PackageRuleCatalogTests.Root, "reference/eu-m1/3.1.1/util/style/ectd-2-0.xsl"), fixture.PathOf("0000/util/style/ectd-2-0.xsl"));
        var (input, lifecycle) = await fixture.SelectAsync("0000");
        var files = await new PackageFileInspector().InspectAsync(input, lifecycle);
        Assert.DoesNotContain(files.Checks.SelectMany(check => check.Findings), finding => finding.RuleId == "DELIVERY-ASSETS" && finding.CheckStatus == CheckStatus.Fail);
        Assert.Contains(files.References, reference => reference.Kind == "Stylesheet" && reference.Exists);
        Assert.DoesNotContain("ich-style", lifecycle.XmlInspections[0].Backbones[0].ResolvedAssets);
        Assert.Contains(files.Checks.SelectMany(check => check.Findings), finding => finding.Code == "STYLESHEET_OUTPUT_RESOURCE_UNEVALUATED");
    }

    [Fact]
    public async Task AggregatePdfLimitPreservesAnIncompleteReport()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var (input, _) = await fixture.SelectAsync("0000", limits: new() { MaxPdfPages = 1 });
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector()).InspectAsync(input, PackageValidationCatalog.UsProfile);
        Assert.Contains(result.Checks.SelectMany(check => check.Findings), finding => finding.Code == "PDF_INSPECTION_LIMIT");
        Assert.NotEqual(CheckStatus.Pass, result.Checks.Single(check => check.RuleId == "PDF-READABLE").Status);
        Assert.False(result.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
    }

    [Fact]
    public async Task OverallDeadlineCannotReturnACompletedDeliveryReport()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var (input, _) = await fixture.SelectAsync("0000");
        var time = new DeadlineTimeProvider();
        var task = new PackageDeliveryInspector(new PdfPigPackageInspector(), time).InspectAsync(input, PackageValidationCatalog.UsProfile);
        time.Expire();
        var exception = await Assert.ThrowsAsync<PackageInputException>(() => task);
        Assert.Equal("DELIVERY_INSPECTION_TIMEOUT", exception.Code);
    }

    private sealed class DeadlineTimeProvider : TimeProvider
    {
        private readonly List<Action> _callbacks = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callbacks.Add(() => callback(state));
            return new InertTimer();
        }
        public void Expire() { foreach (var callback in _callbacks.ToArray()) callback(); }
        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
