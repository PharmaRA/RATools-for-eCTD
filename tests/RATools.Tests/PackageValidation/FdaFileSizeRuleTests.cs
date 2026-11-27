using System.IO.Compression;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaFileSizeRuleTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() =>
        new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));

    [Theory]
    [InlineData("only", CheckStatus.NotEvaluated)]
    [InlineData("before", CheckStatus.Fail)]
    [InlineData("after", CheckStatus.Fail)]
    public async Task UnavailablePdfPrefixDoesNotAbortOtherSizeEvidence(string scenario, CheckStatus expected)
    {
        var unreadable = FileEntry(scenario == "before" ? "0001/a-unreadable.pdf" : "0001/z-unreadable.pdf", 500_000_000);
        var known = FileEntry("0001/m-known.pdf", 500_000_000);
        PackageInputFile[] files = scenario == "only" ? [unreadable] : scenario == "before" ? [unreadable, known] : [known, unreadable];
        var opened = new List<string>();
        await using var input = new PrefixCapture(files, [], "%PDF-", path =>
        {
            opened.Add(path);
            return path == unreadable.LogicalPath ? throw new IOException("Synthetic unavailable captured prefix.") :
                new MemoryStream("%PDF-"u8.ToArray(), writable: false);
        });
        var check = await Inspect(input, 2);
        Assert.Equal(files.Select(file => file.LogicalPath), opened);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Equal(known.LogicalPath, Assert.Single(check.Findings).Location.LogicalPath);
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("read")]
    [InlineData("dispose")]
    public async Task OrdinaryPrefixFailuresRemainUnknown(string scenario)
    {
        await using var input = new PrefixCapture([FileEntry("0001/file.pdf", 500_000_000)], [], "%PDF-", _ => scenario switch
        {
            "missing" => throw new FileNotFoundException("Synthetic missing prefix."),
            "denied" => throw new UnauthorizedAccessException("Synthetic denied prefix."),
            _ => new FaultingPrefixStream(scenario)
        });
        var check = await Inspect(input, 1);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status); Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("integrity")]
    [InlineData("cancel")]
    public async Task IntegrityAndCancellationCannotBecomeOrdinaryUnknown(string scenario)
    {
        Exception failure = scenario switch
        {
            "changed" => new PackageInputChangedException("synthetic-source"),
            "integrity" => new PackageInputException("INPUT-STABLE", "SYNTHETIC", "Synthetic integrity failure."),
            _ => new OperationCanceledException("Synthetic cancellation.")
        };
        await using var input = new PrefixCapture([FileEntry("0001/file.pdf", 500_000_000)], [], "%PDF-", _ => throw failure);
        Assert.Same(failure, await Record.ExceptionAsync(() => Inspect(input, 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringPrefixOpenCannotPublishAResult(bool ioFailure)
    {
        using var cancellation = new CancellationTokenSource();
        await using var input = new PrefixCapture([FileEntry("0001/file.pdf", 500_000_000)], [], "%PDF-", _ =>
        {
            cancellation.Cancel();
            if (ioFailure) throw new IOException("Synthetic I/O after cancellation.");
            return new MemoryStream("%PDF-"u8.ToArray(), writable: false);
        });
        var context = PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(2026, 11, 27));
        Assert.ThrowsAny<OperationCanceledException>(() => FdaFileSizeRule.Inspect(input, PackageValidationCatalog.UsProfile,
            context, 1, cancellation.Token).ToArray());
    }

    [Theory]
    [InlineData(400_000_000L, "file.pdf", 1, CheckStatus.Pass)]
    [InlineData(400_000_001L, "file.pdf", 1, CheckStatus.NotEvaluated)]
    [InlineData(500_000_000L, "file.xpt", 1, CheckStatus.NotEvaluated)]
    [InlineData(500_000_000L, "file.pdf", 0, CheckStatus.NotEvaluated)]
    public async Task PrefixIsNotOpenedOutsideQualifiedSizeAndFindingScope(long size, string name, int remaining, CheckStatus expected)
    {
        await using var input = new PrefixCapture([FileEntry("0001/" + name, size)], [], "",
            _ => throw new InvalidOperationException("Prefix must not be read."));
        Assert.Equal(expected, (await Inspect(input, remaining)).Status);
    }

    private sealed class FaultingPrefixStream(string scenario) : MemoryStream("%PDF-"u8.ToArray(), writable: false)
    {
        public override int Read(Span<byte> buffer) => scenario == "read"
            ? throw new IOException("Synthetic read failure.") : base.Read(buffer);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && scenario == "dispose") throw new IOException("Synthetic close failure.");
        }
    }

    [Theory]
    [InlineData(399_999_999L, "file.pdf", "%PDF-", CheckStatus.Pass)]
    [InlineData(400_000_000L, "data.xpt", "", CheckStatus.Pass)]
    [InlineData(400_000_001L, "file.pdf", "%PDF-", CheckStatus.NotEvaluated)]
    [InlineData(419_430_400L, "file.pdf", "%PDF-", CheckStatus.NotEvaluated)]
    [InlineData(419_430_401L, "file.PDF", "%PDF-1.7", CheckStatus.Fail)]
    [InlineData(419_430_401L, "data.xpt", "", CheckStatus.NotEvaluated)]
    [InlineData(419_430_401L, "file.pdf", "XPT", CheckStatus.NotEvaluated)]
    [InlineData(419_430_401L, "data.xml", "", CheckStatus.NotEvaluated)]
    public async Task UsesManifestBytesAndPositivePdfIdentification(long size, string name, string prefix, CheckStatus expected)
    {
        await using var input = new PrefixCapture([FileEntry("0001/" + name, size)], [], prefix);
        var check = await Inspect(input, 2);
        Assert.Equal(expected, check.Status);
        if (expected != CheckStatus.Fail) { Assert.Empty(check.Findings); return; }
        var finding = Assert.Single(check.Findings);
        Assert.Equal(ValidationSeverity.Info, finding.Severity);
        Assert.Equal("419430401 bytes", finding.Actual);
        Assert.Equal(new ValidationFileAddress(input.SourceId, "0001/" + name), Assert.Single(finding.Location.Files!));
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    public async Task BoundsFindingsAndPreservesKnownFailures(int remaining, CheckStatus expected, int count)
    {
        await using var input = new PrefixCapture([FileEntry("0001/a.xpt", 500_000_000), FileEntry("0001/b.pdf", 500_000_000), FileEntry("0001/c.pdf", 500_000_000)], [], "%PDF-");
        var check = await Inspect(input, remaining);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
    }

    [Fact]
    public async Task SurroundingFilesDoNotEstablishTargetDefects()
    {
        await using var input = new PrefixCapture([FileEntry("0001/index.xml", 100)], [FileEntry("0000/large.pdf", 500_000_000)], "%PDF-");
        Assert.Equal(CheckStatus.Pass, (await Inspect(input, 2)).Status);
    }

    [Fact]
    public async Task UsesExpandedLengthFromCapturedZip()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var archivePath = Path.Combine(root, "submission.zip");
            const long fileLength = 419_430_401;
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                using (var index = archive.CreateEntry("0001/index.xml").Open()) index.Write("<index/>"u8);
                using var content = archive.CreateEntry("0001/document.pdf", CompressionLevel.Fastest).Open();
                content.Write("%PDF-"u8);
                var buffer = new byte[128 * 1024];
                for (long remaining = fileLength - 5; remaining > 0; remaining -= buffer.Length)
                    content.Write(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            }
            Assert.True(new FileInfo(archivePath).Length < 400_000_000);
            var reader = new PackageInputReader(Path.Combine(root, "validation/captures"));
            await using var capture = await reader.ReadAsync(new(Guid.NewGuid(), "1238-zip", archivePath, "0001"), new PackageReadLimits { MaxCompressionRatio = 2000 });
            Assert.Equal(fileLength, Assert.Single(capture.Manifest.Files, file => file.LogicalPath.EndsWith("document.pdf", StringComparison.Ordinal)).Length);
            var check = await Inspect(capture, 1);
            Assert.Equal(CheckStatus.Fail, check.Status);
            Assert.Equal("419430401 bytes", Assert.Single(check.Findings).Actual);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    private static async Task<RuleCheckResult> Inspect(ICapturedPackageInput input, int remaining)
    {
        var context = PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new DateOnly(2026, 11, 18));
        return Assert.Single(FdaFileSizeRule.Inspect(input, PackageValidationCatalog.UsProfile, context, remaining, CancellationToken.None));
    }

    private static PackageInputFile FileEntry(string path, long bytes) => new(path, bytes, new string('a', 64), new string('b', 32));

    [Theory]
    [InlineData("2008-03-09", CheckStatus.NotApplicable, false)]
    [InlineData("2008-03-10", CheckStatus.NotEvaluated, true)]
    public async Task DeliveryReportUsesSelectionDate(string date, CheckStatus status, bool blocks)
    {
        var inspection = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, date: date);
        var report = inspection.CreateReport(PackageValidationMode.Development);
        Assert.Equal(DateOnly.ParseExact(date, "yyyy-MM-dd"), report.Binding.SelectionDate);
        var coverage = Assert.Single(report.Coverage, item => item.RuleId == FdaFileSizeRule.RuleId);
        Assert.Equal(status, coverage.CheckStatus);
        Assert.Equal(blocks, coverage.BlocksReadiness);
    }

    [Fact]
    public async Task DeliveryReportPreservesKnownFailureAndRejectsInapplicableOnEffectiveDate()
    {
        await using var input = new PrefixCapture([FileEntry("0001/file.pdf", 419_430_401)], [], "%PDF-");
        var check = await Inspect(input, 1);
        var catalog = PackageValidationCatalog.Current;
        var binding = new ValidationBinding(input.InputDigest, input.InputDigest, PackageValidationCatalog.UsProfile,
            catalog.Digest, "fixture", PackageValidationMode.Development, input.InputDigest, selectionDate: new DateOnly(2008, 3, 10));
        var report = ValidationReport.Create(catalog, binding, ValidationRunStatus.Completed, [check]);
        var coverage = Assert.Single(report.Coverage, item => item.RuleId == FdaFileSizeRule.RuleId);
        Assert.Equal(CheckStatus.Fail, coverage.CheckStatus);
        Assert.True(coverage.BlocksReadiness);
        Assert.Single(coverage.Findings);
        Assert.Throws<ArgumentException>(() => ValidationReport.Create(catalog, binding, ValidationRunStatus.Completed,
            [new RuleCheckResult(FdaFileSizeRule.RuleId, CheckStatus.NotApplicable, "fixture")]));
    }

    private sealed class PrefixCapture(PackageInputFile[] files, PackageInputFile[] additional, string prefix,
        Func<string, Stream>? open = null) : ICapturedPackageInput
    {
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", files, [], additional, []);
        public string SourceId => "1238-prefix-fixture";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string logicalPath) => open is null ? new MemoryStream(Encoding.ASCII.GetBytes(prefix), writable: false) : open(logicalPath);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("valid", "2008-03-10", CheckStatus.Pass)]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("ich-profile", "2026-11-18", null)]
    public async Task FileStageBindsCapturedSequenceAndSelection(string variant, string? date, CheckStatus? expected)
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date);
        if (expected is null) Assert.DoesNotContain(result.Files.Checks, item => item.RuleId == "FDA47-1238");
        else Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1238").Status);
    }
}
