using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageCapturedDefineXmlReaderTests
{
    [Theory]
    [InlineData("define.xml")]
    [InlineData("content.bin")]
    public async Task ExplicitCaptureIsBoundWithoutTrustingTheExtension(string name)
    {
        await using var capture = new Capture(name: name);
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/" + name);
        Assert.Equal(CapturedDefineStatus.Read, result.Status);
        Assert.Equal(DefineXmlReadStatus.Read, result.Content!.Status);
        Assert.Equal(capture.InputDigest, result.InputDigest);
        Assert.Equal(capture.SourceId, result.SourceId);
        Assert.Equal(64, result.SelectionDigest.Length);
        Assert.Equal(2, capture.Verifications);
        Assert.Equal(1, capture.Reads);
        Assert.True(capture.StreamDisposed);
        Assert.Equal("DM", Assert.Single(result.Content.Datasets).Name);
    }

    [Theory]
    [InlineData("changed-before", "InputChanged")]
    [InlineData("changed-after", "InputChanged")]
    [InlineData("hash", "InputChanged")]
    [InlineData("unavailable", "Unavailable")]
    public async Task MutationsAreNotOrdinaryUnreadableContent(string scenario, string expected)
    {
        await using var capture = new Capture(scenario);
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml");
        Assert.Equal(expected, result.Status.ToString());
        Assert.Null(result.Content);
        Assert.Equal(scenario == "changed-before" ? 0 : 1, capture.Reads);
    }

    [Theory]
    [InlineData("0000/define.xml", "OutsideSelection")]
    [InlineData("0002/define.xml", "OutsideSelection")]
    [InlineData("../define.xml", "OutsideSelection")]
    [InlineData("https://example.invalid/define.xml", "OutsideSelection")]
    [InlineData("0001/missing.xml", "Missing")]
    public async Task MissingAndOutsideSelectionNeverOpenAFile(string path, string expected)
    {
        await using var capture = new Capture();
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, path);
        Assert.Equal(expected, result.Status.ToString());
        Assert.Null(result.Content);
        Assert.Equal(0, capture.Reads);
    }

    [Theory]
    [InlineData("short", "InputChanged")]
    [InlineData("long", "InputChanged")]
    [InlineData("denied", "Unavailable")]
    [InlineData("dispose-io", "Unavailable")]
    [InlineData("verify-io", "Unavailable")]
    [InlineData("huge", "LimitExceeded")]
    public async Task ContentAndIoFailuresNeverPublishUnverifiedFacts(string scenario, string expected)
    {
        await using var capture = new Capture(scenario);
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml");
        Assert.Equal(expected, result.Status.ToString());
        Assert.Null(result.Content);
        if (scenario == "huge") Assert.Equal(0, capture.Reads);
    }

    [Theory]
    [InlineData("invalid", "Invalid")]
    [InlineData("unsupported", "Unsupported")]
    [InlineData("doctype", "Invalid")]
    public async Task StableButUnqualifiedContentIsNotMissingOrEmpty(string scenario, string expected)
    {
        await using var capture = new Capture(scenario);
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml");
        Assert.Equal(CapturedDefineStatus.Read, result.Status);
        Assert.Equal(expected, result.Content!.Status.ToString());
        Assert.False(result.Content.ReadComplete);
        Assert.Empty(result.Content.Datasets);
        Assert.Equal(2, capture.Verifications);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("digest")]
    [InlineData("manifest")]
    [InlineData("limits")]
    public async Task ChangedIdentityAfterReadCannotPublishContent(string field)
    {
        await using var capture = new Capture("identity-" + field);
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml");
        Assert.Equal(CapturedDefineStatus.InputChanged, result.Status);
        Assert.Null(result.Content);
        Assert.Equal(2, capture.Verifications);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("during")]
    [InlineData("io-race")]
    [InlineData("extra-race")]
    public async Task ExternalCancellationAlwaysPropagates(string scenario)
    {
        using var cancellation = new CancellationTokenSource();
        await using var capture = new Capture("cancel-" + scenario, cancel: cancellation.Cancel);
        if (scenario == "before") cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageCapturedDefineXmlReader.ReadAsync(capture,
            "0001/define.xml", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task PartialNonSeekingStreamIsFullyReadAndDisposed()
    {
        await using var capture = new Capture("partial");
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml");
        Assert.Equal(CapturedDefineStatus.Read, result.Status);
        Assert.True(result.Content!.ReadComplete);
        Assert.True(capture.StreamDisposed);
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("depth")]
    [InlineData("characters")]
    public async Task CaptureLimitsCannotBeRelaxedByReaderLimits(string field)
    {
        var limits = field switch
        {
            "nodes" => new PackageReadLimits { MaxXmlNodes = 1 },
            "depth" => new PackageReadLimits { MaxXmlDepth = 1 },
            _ => new PackageReadLimits { MaxXmlCharacters = 1 }
        };
        await using var capture = new Capture(captureLimits: limits);
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml");
        Assert.Equal(CapturedDefineStatus.Read, result.Status);
        Assert.Equal(DefineXmlReadStatus.LimitExceeded, result.Content!.Status);
        Assert.Empty(result.Content.Datasets);
        Assert.Equal(2, capture.Verifications);
    }

    [Fact]
    public async Task ParserLimitsAndStableCaptureParticipateInSelectionIdentity()
    {
        await using var capture = new Capture();
        var first = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml");
        var repeat = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml");
        var limited = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml", new(MaxNodes: 1));
        Assert.Equal(first.SelectionDigest, repeat.SelectionDigest);
        Assert.NotEqual(first.SelectionDigest, limited.SelectionDigest);
        Assert.Equal(CapturedDefineStatus.Read, limited.Status);
        Assert.Equal(DefineXmlReadStatus.LimitExceeded, limited.Content!.Status);
    }

    [Fact]
    public async Task ByteLimitIsCheckedBeforeAllocationOrOpen()
    {
        await using var capture = new Capture();
        var result = await PackageCapturedDefineXmlReader.ReadAsync(capture, "0001/define.xml", new(MaxBytes: 1));
        Assert.Equal(CapturedDefineStatus.LimitExceeded, result.Status);
        Assert.Null(result.Content);
        Assert.Equal(0, capture.Reads);
    }

    [Fact]
    public async Task InvalidReaderLimitsAreNotHiddenByAnAbsentPath()
    {
        await using var capture = new Capture();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PackageCapturedDefineXmlReader.ReadAsync(capture,
            "0001/absent.xml", new(MaxDepth: -1)));
        Assert.Equal(0, capture.Reads);
    }

    private sealed class Capture : ICapturedPackageInput
    {
        private readonly byte[] _bytes;
        private readonly string _scenario;
        private readonly PackageInputManifest _manifest;
        private readonly PackageReadLimits _limits;
        private readonly Action? _cancel;
        public Capture(string scenario = "valid", string name = "define.xml", Action? cancel = null, PackageReadLimits? captureLimits = null)
        {
            _scenario = scenario; _cancel = cancel; _limits = captureLimits ?? new();
            var xml = PackageDefineXmlReaderTests.Sample();
            if (scenario == "invalid") xml += "<broken";
            if (scenario == "unsupported") xml = xml.Replace("2.1.0", "2.0.0", StringComparison.Ordinal);
            if (scenario == "doctype") xml = "<!DOCTYPE ODM SYSTEM 'https://example.invalid/never.dtd'>" + xml;
            _bytes = Encoding.UTF8.GetBytes(xml);
            _manifest = new(Guid.NewGuid(), "0001", [new("0001/" + name, scenario == "huge" ? (long)int.MaxValue + 1 : _bytes.Length,
                Convert.ToHexString(SHA256.HashData(_bytes)).ToLowerInvariant(),
                Convert.ToHexString(MD5.HashData(_bytes)).ToLowerInvariant())], [], [], []);
        }
        public int Reads { get; private set; }
        public int Verifications { get; private set; }
        public bool StreamDisposed { get; private set; }
        public string SourceId => Changed("source") ? "changed" : "captured-define-fixture";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest => Changed("manifest") ? new(_manifest.ApplicationId, "0001", [], [], [], []) : _manifest;
        public string? ArchiveSha256 => null;
        public string InputDigest => Changed("digest") ? new string('a', 64) : _manifest.Digest;
        public PackageReadLimits Limits => Changed("limits") ? new() { MaxXmlNodes = 400000 } : _limits;
        private bool Changed(string field) => _scenario == "identity-" + field && Verifications >= 2;
        public Stream OpenRead(string path)
        {
            Reads++;
            if (_scenario == "cancel-io-race") { _cancel!(); throw new IOException("Cancellation raced with IO failure."); }
            if (_scenario == "unavailable") throw new IOException("Simulated unreadable define bytes.");
            if (_scenario == "denied") throw new UnauthorizedAccessException("Simulated access denial.");
            var bytes = _bytes.ToArray();
            if (_scenario == "hash") bytes[^1] ^= 1;
            if (_scenario == "short") bytes = bytes[..^1];
            if (_scenario == "long") bytes = [.. bytes, 0];
            return new ObservedStream(bytes, () => StreamDisposed = true, _scenario, _cancel);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Verifications++;
            if (_scenario == "changed-before" || _scenario == "changed-after" && Verifications == 2)
                throw new PackageInputChangedException(SourceId);
            if (_scenario == "verify-io" && Verifications == 2) throw new IOException("Unable to reverify.");
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ObservedStream(byte[] bytes, Action disposed, string scenario, Action? cancel) : MemoryStream(bytes, false)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (scenario == "cancel-extra-race" && Position == base.Length)
            { cancel!(); buffer.Span[0] = 0; return ValueTask.FromResult(1); }
            if (scenario == "cancel-during") cancel!();
            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, scenario == "partial" ? 7 : int.MaxValue)], cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            disposed(); base.Dispose(disposing);
            if (scenario == "dispose-io") throw new IOException("Simulated stream close failure.");
        }
    }
}
