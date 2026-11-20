using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageCapturedXptReaderTests
{
    [Theory]
    [InlineData("ordinary-name.bin")]
    [InlineData("ts.xpt")]
    public async Task ReadsExplicitCapturedContentWithoutTrustingTheExtension(string name)
    {
        await using var capture = new Capture(name: name);
        var result = await PackageCapturedXptReader.ReadAsync(capture, "0001/" + name);
        Assert.Equal(CapturedXptStatus.Read, result.Status);
        Assert.Equal(XptReadStatus.Read, result.Content!.Status);
        Assert.Equal(capture.InputDigest, result.InputDigest);
        Assert.Equal(capture.SourceId, result.SourceId);
        Assert.Equal(2, capture.Verifications);
        Assert.Equal(1, capture.Reads);
        Assert.Equal(64, result.SelectionDigest.Length);
        Assert.Equal(1, result.Content.MinPossibleRows);
    }

    [Theory]
    [InlineData("missing", "Missing")]
    [InlineData("outside", "OutsideSelection")]
    [InlineData("changed-before", "InputChanged")]
    [InlineData("changed-after", "InputChanged")]
    [InlineData("short", "InputChanged")]
    [InlineData("long", "InputChanged")]
    [InlineData("hash", "InputChanged")]
    [InlineData("unavailable", "Unavailable")]
    [InlineData("huge", "LimitExceeded")]
    public async Task MissingChangedAndUnreadInputsRemainDistinct(string scenario, string expected)
    {
        await using var capture = new Capture(scenario);
        var path = scenario == "outside" ? "0000/ts.xpt" : scenario == "missing" ? "0001/missing.xpt" : "0001/ts.xpt";
        var result = await PackageCapturedXptReader.ReadAsync(capture, path);
        Assert.Equal(expected, result.Status.ToString());
        Assert.Null(result.Content);
        if (scenario is "outside" or "missing" or "changed-before" or "huge") Assert.Equal(0, capture.Reads);
    }

    [Fact]
    public async Task InvalidTransportIsNotMissingOrAnEmptyTs()
    {
        await using var capture = new Capture("not-xpt");
        var result = await PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt");
        Assert.Equal(CapturedXptStatus.Read, result.Status);
        Assert.Equal(XptReadStatus.Unsupported, result.Content!.Status);
        Assert.False(result.Content.StructureComplete);
        Assert.Equal(2, capture.Verifications);
    }

    [Fact]
    public async Task ParserLimitsAndEncodingParticipateInResultIdentity()
    {
        await using var capture = new Capture();
        var original = await PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt");
        var repeated = await PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt");
        var encoded = await PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt", XptTextEncoding.Utf8);
        var limited = await PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt", limits: new(MaxRows: 0));
        Assert.Equal(original.SelectionDigest, repeated.SelectionDigest);
        Assert.NotEqual(original.SelectionDigest, encoded.SelectionDigest);
        Assert.NotEqual(original.SelectionDigest, limited.SelectionDigest);
        Assert.Equal(XptReadStatus.LimitExceeded, limited.Content!.Status);
        Assert.False(limited.Content.StructureComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeAndDuringReadingPropagates(bool during)
    {
        using var cancellation = new CancellationTokenSource();
        await using var capture = new Capture(cancelRead: during ? cancellation.Cancel : null);
        if (!during) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt", cancellationToken: cancellation.Token));
        Assert.Equal(during ? 1 : 0, capture.Reads);
    }

    [Fact]
    public async Task ExactlyBoundedStreamDoesNotRequireSeeking()
    {
        await using var capture = new Capture("nonseek");
        var result = await PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt");
        Assert.Equal(CapturedXptStatus.Read, result.Status);
        Assert.True(result.Content!.StructureComplete);
        Assert.True(capture.StreamDisposed);
    }

    [Theory]
    [InlineData("cancel-io")]
    [InlineData("cancel-extra")]
    public async Task ExternalCancellationWinsWhenReadAlsoReturnsAnError(string scenario)
    {
        using var cancellation = new CancellationTokenSource();
        await using var capture = new Capture(scenario, cancelRace: cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageCapturedXptReader.ReadAsync(capture,
            "0001/ts.xpt", cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("digest")]
    [InlineData("manifest")]
    [InlineData("limits")]
    public async Task IdentityMutationAfterReadingCannotPublishContent(string field)
    {
        await using var capture = new Capture("identity-" + field);
        var result = await PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt");
        Assert.Equal(CapturedXptStatus.InputChanged, result.Status);
        Assert.Null(result.Content);
        Assert.Equal(2, capture.Verifications);
    }

    [Fact]
    public async Task PartialNonSeekingReadsAreAssembledWithoutLosingBytes()
    {
        await using var capture = new Capture("partial");
        var result = await PackageCapturedXptReader.ReadAsync(capture, "0001/ts.xpt");
        Assert.Equal(CapturedXptStatus.Read, result.Status);
        Assert.Equal(XptReadStatus.Read, result.Content!.Status);
        Assert.True(result.Content.StructureComplete);
        Assert.True(capture.StreamDisposed);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly byte[] _bytes;
        private readonly string _scenario;
        private readonly Action? _cancelRead;
        private readonly Action? _cancelRace;
        private readonly PackageInputManifest _manifest;
        public Capture(string scenario = "valid", string name = "ts.xpt", Action? cancelRead = null, Action? cancelRace = null)
        {
            _scenario = scenario; _cancelRead = cancelRead; _cancelRace = cancelRace;
            _bytes = scenario == "not-xpt" ? Encoding.ASCII.GetBytes(new string('x', 80)) :
                PackageXptReaderTests.Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
            var length = scenario == "huge" ? (long)int.MaxValue + 1 : _bytes.Length;
            _manifest = new(Guid.NewGuid(), "0001", [new("0001/" + name, length, Hash(_bytes), Convert.ToHexString(MD5.HashData(_bytes)).ToLowerInvariant())], [], [], []);
        }
        public int Reads { get; private set; }
        public int Verifications { get; private set; }
        public bool StreamDisposed { get; private set; }
        public string SourceId => Changed("source") ? "changed-source" : "captured-xpt-fixture";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest => Changed("manifest") ? new(_manifest.ApplicationId, "0001", [], [], [], []) : _manifest;
        public string? ArchiveSha256 => null;
        public string InputDigest => Changed("digest") ? new string('a', 64) : _manifest.Digest;
        public PackageReadLimits Limits => Changed("limits") ? new() { MaxXmlNodes = 400000 } : new();
        private bool Changed(string field) => _scenario == "identity-" + field && Verifications >= 2;
        public Stream OpenRead(string path)
        {
            Reads++;
            if (_scenario == "cancel-io") { _cancelRace!(); throw new IOException("Cancellation raced with unreadable bytes."); }
            if (_scenario == "unavailable") throw new IOException("Simulated unreadable captured bytes.");
            var bytes = _bytes.ToArray();
            if (_scenario == "short") bytes = bytes[..^1];
            if (_scenario == "long") bytes = [.. bytes, 0];
            if (_scenario == "hash") bytes[^1] ^= 1;
            return new NonSeekingStream(bytes, _cancelRead, () => StreamDisposed = true,
                _scenario == "cancel-extra" ? _cancelRace : null, _scenario == "partial" ? 7 : int.MaxValue);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Verifications++;
            if (_scenario == "changed-before" || _scenario == "changed-after" && Verifications == 2)
                throw new PackageInputChangedException(SourceId);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class NonSeekingStream(byte[] bytes, Action? cancelRead, Action disposed, Action? cancelExtra = null,
        int chunk = int.MaxValue) : MemoryStream(bytes, false)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancelExtra is not null && Position == base.Length)
            {
                cancelExtra(); buffer.Span[0] = 0;
                return ValueTask.FromResult(1); // Deliberately ignores the token to exercise the caller's result exit.
            }
            cancelRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
        }
        protected override void Dispose(bool disposing) { disposed(); base.Dispose(disposing); }
    }
}
