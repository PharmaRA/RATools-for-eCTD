using RATools.Tests.PackageValidation;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Finalization;
using RATools.Domain.Finalization;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Application.PackageValidation;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.Finalization;

public sealed class ReleaseDeliveryPackageTests : IAsyncDisposable
{
    [Theory]
    [InlineData("payload/0001/m2/file.pdf", false)]
    [InlineData("payload/0001/m2/file.pdf", true)]
    [InlineData("delivery-package.zip", false)]
    [InlineData("delivery-package.zip", true)]
    public async Task PreviewUsesTheVerifiedHandleWhenThePathIsReplacedBeforeResponse(string relativePath, bool range)
    {
        var fixture = NewFixture(); var (staging, manifest) = await fixture.CaptureAsync();
        var committed = await fixture.Store.CommitAsync(staging, manifest.Digest);
        var records = new InMemoryFinalizationStore();
        var snapshot = FinalizedSequence.Create(manifest.ApplicationId, "0001", 1, manifest.Digest, "profile", "3.2.2", null,
            manifest.HistoryBaselineDigest, Guid.NewGuid(), manifest.ValidatedInputManifestDigest, manifest.Validation.ReportSha256,
            PackageValidationMode.Formal, true, [], FinalizationService.ToolVersion, Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        await records.RegisterAsync(snapshot);
        var provider = new ReplaceAfterOpen(new ReleaseCaptureService(new InMemoryPackageValidationStore(), new NoPlanner(), fixture.Store), async () =>
        {
            var path = Path.GetFullPath(Path.Combine(committed.Directory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.StartsWith(Path.GetFullPath(committed.Directory) + Path.DirectorySeparatorChar, path,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.Equal(0, (int)(File.GetAttributes(path) & FileAttributes.ReparsePoint));
            File.Move(path, path + ".retained");
            await File.WriteAllTextAsync(path, "substituted after verification");
        });
        var service = new FinalizationService(records, provider, new NoRevisions());
        var controller = new RATools.Api.Controllers.FinalizationsController(service, records, null!)
        { ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.FileStreamResult>(await controller.File(snapshot.Id, relativePath, CancellationToken.None));
        await using var stream = result.FileStream;
        var expected = manifest.Files.Single(file => file.RelativePath == relativePath);
        Assert.Equal(0, stream.Position);
        Assert.Equal('"' + expected.Sha256 + '"', controller.Response.Headers.ETag.ToString());
        Assert.Equal(expected.Sha256, Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant());
        Assert.True(result.EnableRangeProcessing);
        stream.Position = 0;
        using var expectedBytes = new MemoryStream(); await stream.CopyToAsync(expectedBytes); stream.Position = 0;
        using var services = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        controller.HttpContext.RequestServices = services;
        controller.Request.Method = "GET";
        if (range) controller.Request.Headers.Range = "bytes=1-3";
        using var body = new MemoryStream(); controller.Response.Body = body;
        await result.ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext(controller.HttpContext,
            new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        Assert.Equal(range ? 206 : 200, controller.Response.StatusCode);
        Assert.Equal(range ? expectedBytes.ToArray()[1..4] : expectedBytes.ToArray(), body.ToArray());
        Assert.False(stream.CanRead);
    }

    private sealed class ReplaceAfterOpen(IFinalizationCaptureProvider inner, Func<Task> replace) : IFinalizationCaptureProvider
    {
        public Task<IFinalizationCapture> CaptureAsync(FinalizeSequenceRequest request, CancellationToken cancellationToken = default) => inner.CaptureAsync(request, cancellationToken);
        public Task<ReleaseManifest> ReadManifestAsync(FinalizedSequence snapshot, CancellationToken cancellationToken = default) => inner.ReadManifestAsync(snapshot, cancellationToken);
        public async Task<ReleaseFileContent> OpenFileAsync(FinalizedSequence snapshot, string relativePath, CancellationToken cancellationToken = default)
        {
            var file = await inner.OpenFileAsync(snapshot, relativePath, cancellationToken);
            try { await replace(); return file; }
            catch { if (file is IAsyncDisposable disposable) await disposable.DisposeAsync(); throw; }
        }
    }

    private readonly List<Fixture> _fixtures = [];
    private Fixture NewFixture()
    {
        var fixture = new Fixture();
        _fixtures.Add(fixture);
        return fixture;
    }

    [Fact]
    public async Task PayloadChangedAfterZipCaptureIsRefusedBeforeManifestCompletion()
    {
        var fixture = NewFixture();
        var (staging, manifest) = await fixture.CaptureAsync(complete: false);
        await File.AppendAllTextAsync(Path.Combine(staging.Directory, "payload", "0001", "index.xml"), "changed");

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.CompleteAsync(staging, manifest));
    }

    [Fact]
    public async Task ZipEntriesMustReproduceThePayloadInventoryEvenIfTheWholeZipDigestMatches()
    {
        var fixture = NewFixture();
        var (staging, _) = await fixture.CaptureAsync(complete: false);
        var path = Path.Combine(staging.Directory, "delivery-package.zip");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("0001/wrong.xml").Open()))
            writer.Write("not the manifest payload");
        var bytes = await File.ReadAllBytesAsync(path);
        var altered = staging with { Files = staging.Files.Select(file => file.Kind != ReleaseFileKind.DeliveryPackage ? file :
            file with { Length = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                Md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant() }).ToArray() };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.CompleteAsync(altered, fixture.Manifest(altered)));
    }

    [Fact]
    public async Task ZipEntryBytesMustMatchThePayloadEvenWhenItsNameAndWholeZipDigestMatch()
    {
        var fixture = NewFixture();
        var (staging, _) = await fixture.CaptureAsync(complete: false);
        var path = Path.Combine(staging.Directory, "delivery-package.zip");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var file in staging.Files.Where(file => file.Kind == ReleaseFileKind.Payload))
            {
                using var writer = new StreamWriter(archive.CreateEntry(file.RelativePath[8..]).Open());
                writer.Write("different bytes");
            }
        }
        var bytes = await File.ReadAllBytesAsync(path);
        var altered = staging with { Files = staging.Files.Select(file => file.Kind != ReleaseFileKind.DeliveryPackage ? file :
            file with { Length = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                Md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant() }).ToArray() };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.CompleteAsync(altered, fixture.Manifest(altered)));
    }

    [Fact]
    public async Task DiscardRefusesAReplacedStagingAncestor()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fixture = NewFixture();
        var (staging, _) = await fixture.CaptureAsync();
        var stagingRoot = Path.GetDirectoryName(staging.Directory)!;
        var retainedRoot = stagingRoot + "-retained";
        Directory.Move(stagingRoot, retainedRoot);
        // The task creates this junction only to test refusal. It is retained as evidence:
        // cleanup must never recurse into or move a link without a separate review.
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"New-Item -ItemType Junction -Path '{stagingRoot}' -Target '{retainedRoot}' | Out-Null");
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        Assert.NotEqual(0, (int)(File.GetAttributes(stagingRoot) & FileAttributes.ReparsePoint));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.DiscardAsync(staging));
        Assert.True(File.Exists(Path.Combine(retainedRoot, Path.GetFileName(staging.Directory), "delivery-package.zip")));
    }

    [Fact]
    public async Task DeliveryRegistrationAcceptsOnlyTheMeasuredImmutableZipDigest()
    {
        var fixture = NewFixture();
        var (staging, manifest) = await fixture.CaptureAsync();
        var committed = await fixture.Store.CommitAsync(staging, manifest.Digest);
        var records = new InMemoryFinalizationStore();
        var snapshot = FinalizedSequence.Create(manifest.ApplicationId, "0001", 1, manifest.Digest, "profile", "3.2.2", null,
            manifest.HistoryBaselineDigest, Guid.NewGuid(), manifest.ValidatedInputManifestDigest, manifest.Validation.ReportSha256,
            PackageValidationMode.Formal, true, [], FinalizationService.ToolVersion, Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        await records.RegisterAsync(snapshot);
        var captures = new ReleaseCaptureService(new InMemoryPackageValidationStore(), new NoPlanner(), fixture.NewStore());
        var service = new FinalizationService(records, captures, new NoRevisions());
        var package = Assert.Single(manifest.Files, file => file.Kind == ReleaseFileKind.DeliveryPackage);

        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => service.RegisterDeliveryAsync(snapshot.Id,
            new string('0', 64), "gateway", null, null, "operator"));
        var accepted = await service.RegisterDeliveryAsync(snapshot.Id, package.Sha256, "gateway", null, null, "operator");
        Assert.Equal(package.Sha256, accepted.PackageSha256);
        await File.AppendAllTextAsync(Path.Combine(committed.Directory, "delivery-package.zip"), "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RegisterDeliveryAsync(snapshot.Id,
            package.Sha256, "gateway", null, null, "operator"));
        Assert.Single(await records.ListDeliveriesAsync(snapshot.Id));
    }

    [Fact]
    public async Task DeliveryZipIsManifestBoundAndRecomputedAfterStoreRestart()
    {
        var fixture = NewFixture();
        var (staging, manifest) = await fixture.CaptureAsync();
        var package = Assert.Single(manifest.Files, file => file.Kind == ReleaseFileKind.DeliveryPackage);
        Assert.Equal("delivery-package.zip", package.RelativePath);
        var committed = await fixture.Store.CommitAsync(staging, manifest.Digest);
        var restarted = fixture.NewStore();

        await using var content = await restarted.OpenFileAsync(manifest.ApplicationId, "0001", manifest.Digest, package.RelativePath);

        Assert.Equal("application/zip", content.MediaType);
        Assert.Equal(package.Sha256, content.Sha256);
        Assert.Equal(package.Sha256, Convert.ToHexString(await SHA256.HashDataAsync(content.Content)).ToLowerInvariant());
        content.Content.Position = 0;
        Assert.Equal(package.Md5, Convert.ToHexString(await MD5.HashDataAsync(content.Content)).ToLowerInvariant());
        Assert.Equal(package.Length, content.Content.Length);
        content.Content.Position = 0;
        using var archive = new ZipArchive(content.Content, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal(["0001/index.xml", "0001/m2/file.pdf"], archive.Entries.Select(entry => entry.FullName).ToArray());
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("manifest", StringComparison.Ordinal) || entry.FullName.Contains("validation-report", StringComparison.Ordinal));
        Assert.True(Directory.Exists(committed.Directory));
    }

    [Fact]
    public async Task IdenticalPayloadProducesTheSameZipBytesRegardlessOfCaptureTimeOrOrder()
    {
        var fixture = NewFixture();
        var first = await fixture.CaptureAsync();
        var second = await fixture.CaptureAsync(reverse: true);

        var firstZip = Assert.Single(first.Staging.Files, file => file.Kind == ReleaseFileKind.DeliveryPackage);
        var secondZip = Assert.Single(second.Staging.Files, file => file.Kind == ReleaseFileKind.DeliveryPackage);
        Assert.Equal(firstZip, secondZip);
        Assert.Equal(first.Manifest.Digest, second.Manifest.Digest);
        await fixture.Store.CommitAsync(first.Staging, first.Manifest.Digest);
        Assert.False((await fixture.Store.CommitAsync(second.Staging, second.Manifest.Digest)).Created);
    }

    [Fact]
    public async Task TamperedZipCannotBeReadAsCapturedDeliveryEvidence()
    {
        var fixture = NewFixture();
        var (staging, manifest) = await fixture.CaptureAsync();
        var committed = await fixture.Store.CommitAsync(staging, manifest.Digest);
        await File.AppendAllTextAsync(Path.Combine(committed.Directory, "delivery-package.zip"), "changed");

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.NewStore().OpenFileAsync(
            manifest.ApplicationId, "0001", manifest.Digest, "delivery-package.zip"));
    }

    [Fact]
    public async Task LegacyManifestWithoutAZipBindingRemainsReadableButCannotServeAnUnlistedZip()
    {
        var fixture = NewFixture();
        var (staging, current) = await fixture.CaptureAsync();
        var legacy = fixture.Manifest(staging with { Files = staging.Files.Where(file => file.Kind != ReleaseFileKind.DeliveryPackage).ToArray() });
        var restored = ReleaseManifest.FromJson(legacy.ToUtf8Json(), legacy.ApplicationId, "0001", legacy.Digest);
        Assert.DoesNotContain(restored.Files, file => file.Kind == ReleaseFileKind.DeliveryPackage);
        var root = Path.Combine(fixture.Root, legacy.ApplicationId.ToString("N"), "_releases", "0001", legacy.Digest);
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, "manifest.json"), legacy.ToUtf8Json());
        await File.WriteAllTextAsync(Path.Combine(root, "delivery-package.zip"), "unbound legacy bytes");

        await Assert.ThrowsAsync<ReleaseFileNotFoundException>(() => fixture.Store.OpenFileAsync(
            current.ApplicationId, "0001", legacy.Digest, "delivery-package.zip"));
        await fixture.Store.DiscardAsync(staging);
    }

    private sealed class Fixture
    {
        public string Root { get; } = Path.Combine(RetainedPackageTestFiles.ScratchParent, "ratools-qa03-evidence", Guid.NewGuid().ToString("N"));
        private Guid ApplicationId { get; } = Guid.NewGuid();
        public LocalReleaseCaptureStore Store => NewStore();
        public LocalReleaseCaptureStore NewStore() => new(Options.Create(new BackboneOutputOptions { RootPath = Root }));

        public async Task<(ReleaseCaptureStaging Staging, ReleaseManifest Manifest)> CaptureAsync(bool reverse = false, bool complete = true)
        {
            ReleaseCaptureFile[] files =
            [
                ReleaseCaptureFile.Generated("payload/0001/index.xml", Encoding.UTF8.GetBytes("<index />")),
                ReleaseCaptureFile.Generated("payload/0001/m2/file.pdf", Encoding.UTF8.GetBytes("stable PDF bytes")),
                ReleaseCaptureFile.Generated("validation-report.json", Encoding.UTF8.GetBytes("{}"), ReleaseFileKind.ValidationReport)
            ];
            var staging = await Store.SaveAsync(new(ApplicationId, "0001", reverse ? files.Reverse().ToArray() : files));
            var manifest = Manifest(staging);
            if (complete) await Store.CompleteAsync(staging, manifest);
            return (staging, manifest);
        }

        public ReleaseManifest Manifest(ReleaseCaptureStaging staging) => new(ApplicationId, "0001", 1,
            FinalizationService.ToolVersion, new string('a', 64), JsonSerializer.SerializeToElement(new
            {
                applicationId = ApplicationId.ToString("D"), targetSequence = "0001", digest = new string('b', 64), entries = Array.Empty<object>()
            }), new string('b', 64), new(new string('c', 64), "Development", "profile", "3.2.2", null,
                new string('d', 64), "engine", null), [], staging.Files, [], [], []);
    }

    public ValueTask DisposeAsync()
    {
        if (!OperatingSystem.IsWindows()) return ValueTask.CompletedTask;
        foreach (var fixture in _fixtures)
        {
            if (!Directory.Exists(fixture.Root)) continue;
            var full = Path.GetFullPath(fixture.Root);
            Assert.StartsWith(Path.Combine(RetainedPackageTestFiles.ScratchParent, "ratools-qa03-evidence") + Path.DirectorySeparatorChar, full, StringComparison.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(full);
            while (pending.TryPop(out var directory))
            {
                Assert.Equal(0, (int)(File.GetAttributes(directory) & FileAttributes.ReparsePoint));
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        Console.WriteLine($"RETAIN controlled link evidence: {full}");
                        return ValueTask.CompletedTask;
                    }
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
            }
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(full, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        return ValueTask.CompletedTask;
    }

    private sealed class NoPlanner : IReleaseCapturePlanner
    {
        public Task<ReleaseCapturePreparation> PlanAsync(FinalizeSequenceRequest request, PackageValidationRun run,
            StoredPackageValidationRequest storedRequest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoRevisions : IWorkspaceRevisionStore
    {
        public Task<long?> GetRevisionAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IAsyncDisposable> LockApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AdvanceAsync(Guid applicationId, string sequenceNumber, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
