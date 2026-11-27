using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaUtilityReadFailureTests
{
    private static readonly Lazy<Task<PackageStandardsContext>> Context = new(async () =>
        PackageStandardsContext.FromSnapshot(await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"), selectionDate: new(2026, 11, 27)));

    [Theory]
    [InlineData("only", CheckStatus.NotEvaluated)]
    [InlineData("before", CheckStatus.Fail)]
    [InlineData("after", CheckStatus.Fail)]
    public async Task UnavailableMetadataDoesNotAbortOtherUtilityEvidence(string scenario, CheckStatus expected)
    {
        var unreadable = scenario == "before" ? "0001/util/a/.DS_Store" : "0001/util/z/.DS_Store";
        const string known = "0001/util/m/.DS_Store";
        string[] paths = scenario == "only" ? [unreadable] : scenario == "before" ? [unreadable, known] : [known, unreadable];
        var opened = new List<string>();
        await using var input = new Input(paths, path =>
        {
            opened.Add(path);
            return path == unreadable ? throw new IOException("Synthetic unavailable metadata.") : Header();
        });
        var check = Assert.Single(FdaUtilityFileRule.Inspect(input, PackageValidationCatalog.UsProfile, await Context.Value, 2, CancellationToken.None));
        Assert.Equal(paths, opened);
        Assert.Equal(expected, check.Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(check.Findings, item => item.Location.LogicalPath == unreadable).CheckStatus);
        if (expected == CheckStatus.Fail)
            Assert.Equal(known, Assert.Single(check.Findings, item => item.CheckStatus == CheckStatus.Fail).Location.LogicalPath);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("read")]
    [InlineData("dispose")]
    public async Task OrdinaryReadFailuresCannotProveFinderIdentity(string scenario)
    {
        await using var input = new Input(["0001/util/style/.DS_Store"], _ => scenario switch
        {
            "missing" => throw new FileNotFoundException("Synthetic unavailable metadata."),
            "denied" => throw new UnauthorizedAccessException("Synthetic denied metadata."),
            _ => new FaultingStream(scenario)
        });
        var check = Assert.Single(FdaUtilityFileRule.Inspect(input, PackageValidationCatalog.UsProfile, await Context.Value, 1, CancellationToken.None));
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(check.Findings).CheckStatus);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("integrity")]
    [InlineData("cancel")]
    public async Task IntegrityAndCancellationAreNotSwallowed(string scenario)
    {
        Exception failure = scenario switch
        {
            "changed" => new PackageInputChangedException("synthetic-source"),
            "integrity" => new PackageInputException("INPUT-STABLE", "SYNTHETIC", "Synthetic integrity failure."),
            _ => new OperationCanceledException("Synthetic cancellation.")
        };
        await using var input = new Input(["0001/util/style/.DS_Store"], _ => throw failure);
        var context = await Context.Value;
        Assert.Same(failure, Record.Exception(() => FdaUtilityFileRule.Inspect(input,
            PackageValidationCatalog.UsProfile, context, 1, CancellationToken.None).ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringOpenCannotPublishUnknown(bool ioFailure)
    {
        using var cancellation = new CancellationTokenSource();
        await using var input = new Input(["0001/util/style/.DS_Store"], _ =>
        {
            cancellation.Cancel();
            if (ioFailure) throw new IOException("Synthetic I/O after cancellation.");
            return Header();
        });
        var context = await Context.Value;
        Assert.ThrowsAny<OperationCanceledException>(() => FdaUtilityFileRule.Inspect(input,
            PackageValidationCatalog.UsProfile, context, 1, cancellation.Token).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleFindingSlotPrioritizesKnownFailure(bool unreadableFirst)
    {
        var unreadable = unreadableFirst ? "0001/util/a/.DS_Store" : "0001/util/z/.DS_Store";
        const string known = "0001/util/m/.DS_Store";
        var opened = new List<string>();
        await using var input = new Input([unreadable, known], path =>
        {
            opened.Add(path);
            return path == unreadable ? throw new IOException("Synthetic unavailable metadata.") : Header();
        });
        var check = Assert.Single(FdaUtilityFileRule.Inspect(input, PackageValidationCatalog.UsProfile, await Context.Value, 1, CancellationToken.None));
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal(known, Assert.Single(check.Findings).Location.LogicalPath);
        Assert.Equal(unreadableFirst ? new[] { unreadable, known } : [known], opened);
    }

    [Theory]
    [InlineData("0001/util/style/.DS_Store", 0, CheckStatus.NotEvaluated)]
    [InlineData("0001/util/style/unknown.mod", 1, CheckStatus.NotEvaluated)]
    [InlineData("0001/m3/.DS_Store", 1, CheckStatus.Pass)]
    public async Task NonCandidateOrNoCapacityDoesNotOpenBytes(string path, int remaining, CheckStatus expected)
    {
        await using var input = new Input([path], _ => throw new InvalidOperationException("Bytes must not be read."));
        var check = Assert.Single(FdaUtilityFileRule.Inspect(input, PackageValidationCatalog.UsProfile, await Context.Value, remaining, CancellationToken.None));
        Assert.Equal(expected, check.Status);
    }

    private sealed class FaultingStream(string scenario) : MemoryStream("\0\0\0\u0001Bud1"u8.ToArray(), writable: false)
    {
        public override int Read(Span<byte> buffer) => scenario == "read"
            ? throw new IOException("Synthetic metadata read failure.") : base.Read(buffer);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && scenario == "dispose") throw new IOException("Synthetic metadata close failure.");
        }
    }

    private static MemoryStream Header() => new("\0\0\0\u0001Bud1"u8.ToArray(), writable: false);

    private sealed class Input(string[] paths, Func<string, Stream> open) : ICapturedPackageInput
    {
        public string SourceId => "fda1314-read-fixture";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001",
            paths.Select(path => new PackageInputFile(path, 8, new string('a', 64), new string('b', 32))), [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => open(path);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
