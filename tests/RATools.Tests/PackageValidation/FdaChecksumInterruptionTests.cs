using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaChecksumInterruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexReadTimeoutPreservesPreviouslyKnownMismatch(bool mismatch)
    {
        var clock = new DeadlineClock();
        await using var capture = new ChecksumCapture(mismatch, clock.Expire);
        var context = await Context();
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var files = await new PackageFileInspector(clock, context).InspectAsync(new(packages), lifecycle);
        Assert.Contains(files.Checks.SelectMany(check => check.Findings), finding => finding.Code == "FILE_INSPECTION_TIMEOUT");
        var check = Assert.Single(files.Checks, check => check.RuleId == "FDA47-1374");
        Assert.Equal(mismatch ? CheckStatus.Fail : CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(mismatch ? 1 : 0, check.Findings.Count);
        if (mismatch) AssertLeafMismatch(Assert.Single(check.Findings));
    }

    [Fact]
    public async Task ExplicitCallerCancellationStillPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        await using var capture = new ChecksumCapture(true, cancellation.Cancel);
        var context = await Context();
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PackageFileInspector(standards: context)
            .InspectAsync(new(packages), lifecycle, cancellation.Token));
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated, 0)]
    [InlineData(1, CheckStatus.Fail, 1)]
    public async Task SharedBudgetPreservesFirstMismatch(int remaining, CheckStatus expected, int count)
    {
        await using var capture = new ChecksumCapture(true, () => { });
        var context = await Context();
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        var check = await FdaChecksumMatchRule.InspectAsync(packages, lifecycle, context, remaining, CancellationToken.None);
        Assert.NotNull(check);
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        if (count == 1) AssertLeafMismatch(Assert.Single(check.Findings));
    }

    private static void AssertLeafMismatch(ValidationFinding finding)
    {
        Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
        Assert.Equal("content", finding.Location.LeafId);
        Assert.Equal("@checksum", finding.Location.FieldPath);
        Assert.Equal(new string('0', 32), finding.Actual);
        Assert.Equal(Convert.ToHexString(MD5.HashData("content"u8.ToArray())).ToLowerInvariant(), finding.Expected);
    }

    private static async Task<PackageStandardsContext> Context() => PackageStandardsContext.FromSnapshot(
        await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"),
        selectionDate: new DateOnly(2026, 11, 19));

    private sealed class ChecksumCapture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly Action _onIndexRead;
        public ChecksumCapture(bool mismatch, Action onIndexRead)
        {
            _onIndexRead = onIndexRead;
            var app = Guid.NewGuid();
            var input = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = input.Manifest.Files.ToDictionary(file => file.LogicalPath, file =>
            {
                using var stream = input.OpenRead(file.LogicalPath); using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
            }, StringComparer.Ordinal);
            _files["0001/m2/content.txt"] = "content"u8.ToArray();
            var declared = mismatch ? new string('0', 32) : Md5(_files["0001/m2/content.txt"]);
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes("<!DOCTYPE ectd:ectd SYSTEM \"util/dtd/ich-ectd-3-2.dtd\"><ectd:ectd xmlns:ectd=\"http://www.ich.org/ectd\" xmlns:xlink=\"http://www.w3c.org/1999/xlink\" dtd-version=\"3.2\"><m2-common-technical-document-summaries><leaf ID=\"content\" operation=\"new\" checksum-type=\"md5\" checksum=\"" + declared + "\" xlink:href=\"m2/content.txt\"><title>Content</title></leaf></m2-common-technical-document-summaries></ectd:ectd>");
            _files["0001/index-md5.txt"] = Encoding.ASCII.GetBytes(new string('0', 32));
            Manifest = new(app, "0001", _files.Select(pair => new PackageInputFile(pair.Key, pair.Value.Length,
                Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant(), Md5(pair.Value))), [], [], []);
        }
        public PackageInputManifest Manifest { get; }
        public string SourceId => "1374-interruption";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => path.EndsWith("/index-md5.txt", StringComparison.Ordinal)
            ? new InterruptingStream(_files[path], _onIndexRead) : new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        private static string Md5(byte[] value) => Convert.ToHexString(MD5.HashData(value)).ToLowerInvariant();
    }

    private sealed class InterruptingStream(byte[] bytes, Action onRead) : MemoryStream(bytes, false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { onRead(); return base.ReadAsync(buffer, cancellationToken); }
    }

    private sealed class DeadlineClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; _state = state; return new InertTimer(); }
        public void Expire() => _callback!(_state);
        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
