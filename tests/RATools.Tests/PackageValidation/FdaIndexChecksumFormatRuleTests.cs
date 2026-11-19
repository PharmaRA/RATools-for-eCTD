using System.Text;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaIndexChecksumFormatRuleTests
{
    [Theory]
    [InlineData("truncated", "lower", CheckStatus.Pass)]
    [InlineData("unread", "lower", CheckStatus.Pass)]
    [InlineData("truncated", "invalid-hex", CheckStatus.Fail)]
    [InlineData("unread", "invalid-hex", CheckStatus.Fail)]
    public async Task ThirtyTwoByteReadDoesNotDependOnXmlCoverage(string variant, string scenario, CheckStatus expected)
        => Assert.Equal(expected, Assert.Single((await Inspect(scenario, variant)).Files.Checks, item => item.RuleId == "FDA47-1391").Status);

    [Theory]
    [InlineData("length", CheckStatus.Fail, 0)]
    [InlineData("fragmented", CheckStatus.Pass, 32)]
    [InlineData("extra", CheckStatus.NotEvaluated, 33)]
    [InlineData("short-stream", CheckStatus.NotEvaluated, 31)]
    [InlineData("open-error", CheckStatus.NotEvaluated, 0)]
    [InlineData("read-error", CheckStatus.NotEvaluated, 0)]
    public async Task BoundsReadsAndRetainsUnavailableEvidence(string scenario, CheckStatus expected, int bytesRead)
    {
        await using var capture = new ControlledCapture(scenario);
        var check = await FdaIndexChecksumFormatRule.InspectAsync(capture, PackageValidationCatalog.UsProfile, await Context(), 1, CancellationToken.None);
        Assert.NotNull(check);
        Assert.Equal(expected, check.Status);
        Assert.Equal(bytesRead, capture.BytesRead);
        Assert.True(capture.BytesRead <= 33);
        if (scenario == "length") Assert.Equal(0, capture.OpenCount);
    }

    [Fact]
    public async Task CancellationDuringReadPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        await using var capture = new ControlledCapture("fragmented", cancellation.Cancel);
        var context = await Context();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FdaIndexChecksumFormatRule.InspectAsync(capture,
            PackageValidationCatalog.UsProfile, context, 1, cancellation.Token));
        Assert.Equal(1, capture.OpenCount);
    }

    private static async Task<PackageStandardsContext> Context() => PackageStandardsContext.FromSnapshot(
        await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"),
        selectionDate: new DateOnly(2026, 11, 19));

    private sealed class ControlledCapture(string scenario, Action? onRead = null) : ICapturedPackageInput
    {
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001",
            [new PackageInputFile("0001/index-md5.txt", scenario == "length" ? 33 : 32, new string('a', 64), new string('a', 32))], [], [], []);
        public string SourceId => "1391-controlled";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public int OpenCount { get; private set; }
        public int BytesRead { get; private set; }
        public Stream OpenRead(string logicalPath)
        {
            OpenCount++;
            if (scenario == "open-error") throw new IOException("Injected read-open failure.");
            var length = scenario == "extra" ? 33 : scenario == "short-stream" ? 31 : 32;
            return new CountingStream(Enumerable.Repeat((byte)'a', length).ToArray(), scenario == "read-error", onRead, count => BytesRead += count);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingStream(byte[] bytes, bool fail, Action? onRead, Action<int> counted) : MemoryStream(bytes, false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (fail) throw new IOException("Injected stream failure.");
            var count = await base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
            counted(count);
            return count;
        }
    }

    [Theory]
    [InlineData("lower", CheckStatus.Pass)]
    [InlineData("upper", CheckStatus.Pass)]
    [InlineData("different-value", CheckStatus.Pass)]
    [InlineData("missing", CheckStatus.NotEvaluated)]
    [InlineData("empty", CheckStatus.Fail)]
    [InlineData("short", CheckStatus.Fail)]
    [InlineData("long", CheckStatus.Fail)]
    [InlineData("space", CheckStatus.Fail)]
    [InlineData("leading-space", CheckStatus.Fail)]
    [InlineData("lf", CheckStatus.Fail)]
    [InlineData("crlf", CheckStatus.Fail)]
    [InlineData("invalid-hex", CheckStatus.Fail)]
    [InlineData("bom", CheckStatus.Fail)]
    [InlineData("utf16", CheckStatus.Fail)]
    [InlineData("nonascii", CheckStatus.Fail)]
    public async Task RequiresExactlyThirtyTwoAsciiHexBytes(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1391");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal("0001/index-md5.txt", finding.Location.LogicalPath);
            Assert.Equal("0001/index-md5.txt", Assert.Single(finding.Location.Files!).LogicalPath);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1374").Status);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "2026-11-19", CheckStatus.Fail)]
    [InlineData("unread", "2026-11-19", CheckStatus.Fail)]
    [InlineData("budget", "2026-11-19", CheckStatus.NotEvaluated)]
    public async Task FormatCheckDoesNotRequireParsedXmlButDoesRequireSourceDateAndBudget(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect("crlf", variant, date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1391").Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(item => item.Findings.Count) <= 2);
    }

    [Theory]
    [InlineData(false, CheckStatus.NotEvaluated)]
    [InlineData(true, CheckStatus.Pass)]
    public async Task HistoricalFileCannotSupplyOrInvalidateTargetFormat(bool supplyTarget, CheckStatus expected)
    {
        var result = await Inspect(supplyTarget ? "lower" : "missing", "history-1391");
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1391").Status);
    }

    [Fact]
    public async Task IchProfileDoesNotAcquireFdaCoverage()
        => Assert.DoesNotContain((await Inspect("crlf", "ich-profile")).Checks, item => item.RuleId == "FDA47-1391");

    private static Task<RATools.Application.PackageValidation.PackageDeliveryInspection> Inspect(string scenario, string variant = "valid", string? date = "2026-11-19")
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date, editSource: source =>
        {
            const string valid = "0123456789abcdef0123456789abcdef";
            byte[] bytes = scenario switch
            {
                "empty" => [], "short" => Encoding.ASCII.GetBytes(valid[..31]), "long" => Encoding.ASCII.GetBytes(valid + "0"),
                "space" => Encoding.ASCII.GetBytes(valid + " "), "leading-space" => Encoding.ASCII.GetBytes(" " + valid[1..]),
                "lf" => Encoding.ASCII.GetBytes(valid + "\n"), "crlf" => Encoding.ASCII.GetBytes(valid + "\r\n"),
                "invalid-hex" => Encoding.ASCII.GetBytes("g" + valid[1..]), "upper" => Encoding.ASCII.GetBytes(valid.ToUpperInvariant()),
                "different-value" => Encoding.ASCII.GetBytes(new string('0', 32)),
                "bom" => [0xef, 0xbb, 0xbf, .. Encoding.ASCII.GetBytes(valid)],
                "utf16" => Encoding.Unicode.GetBytes(valid), "nonascii" => Enumerable.Repeat((byte)0xff, 32).ToArray(),
                _ => Encoding.ASCII.GetBytes(valid)
            };
            if (scenario != "missing") File.WriteAllBytes(Path.Combine(source, "0001/index-md5.txt"), bytes);
            if (variant == "history-1391") File.WriteAllBytes(Path.Combine(source, "0000/index-md5.txt"), "invalid"u8.ToArray());
        });
}
