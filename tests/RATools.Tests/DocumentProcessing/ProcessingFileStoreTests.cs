using System.Security.Cryptography;
using RATools.Application.Abstractions.Security;
using RATools.Application.DocumentProcessing;
using RATools.Application.Publishing;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Tests.DocumentProcessing;

public sealed class ProcessingFileStoreTests
{
    [Fact]
    public async Task CaptureAndAdoptionUseIndependentBytesAndPreservePublishedHref()
    {
        var directory = Path.GetFullPath($".artifacts/p5-06-file-tests/{Guid.NewGuid():N}");
        var application = new SubmissionApplication(Guid.NewGuid().ToString("N"), "US", "Synthetic", directory, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "test");
        var original = Path.Combine(directory, "0001", "m3", "source.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        var bytes = "%PDF-1.7 original synthetic bytes"u8.ToArray();
        await File.WriteAllBytesAsync(original, bytes);
        var document = new SubmissionDocument("source.pdf", "application/pdf", bytes.Length,
            Hex(SHA256.HashData(bytes)), Hex(MD5.HashData(bytes)), original);
        var store = new ProcessingFileStore(new TestPaths(directory), new PdfProcessingExecutionOptions());
        var version = await store.CaptureAsync(application, "0001", document);
        Assert.NotEqual(original, version.StoragePath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(version.StoragePath));
        var materialized = await store.MaterializeAsync(application, "0001", Guid.NewGuid(), version, "m3/source.pdf");
        Assert.Equal("m3/source.pdf", PublishOutputNaming.BuildPublishedDocumentRelativePath(materialized, "0001"));
        Assert.NotEqual(original, materialized.StoragePath);
        Assert.NotEqual(version.StoragePath, materialized.StoragePath);
        await File.WriteAllTextAsync(materialized.StoragePath, "edited working copy");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(version.StoragePath));
        await store.VerifyAsync(version);
    }

    [Fact]
    public async Task ChangedSourceAndChangedResultCannotBeConsumed()
    {
        var directory = Path.GetFullPath($".artifacts/p5-06-file-tests/{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "0001"));
        var source = Path.Combine(directory, "0001", "source.pdf");
        await File.WriteAllTextAsync(source, "changed");
        var application = new SubmissionApplication(Guid.NewGuid().ToString("N"), "US", "Synthetic", directory, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "test");
        var document = new SubmissionDocument("source.pdf", "application/pdf", 7, new string('a', 64), new string('b', 32), source);
        var store = new ProcessingFileStore(new TestPaths(directory), new PdfProcessingExecutionOptions());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.CaptureAsync(application, "0001", document));
        var version = DocumentVersion.CreateSource(document.Id, application.Id, "0001", document.FileName, document.MediaType,
            document.FileSize, document.Sha256, document.Md5, source, DateTime.UtcNow);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync(version));
        await Assert.ThrowsAsync<ArgumentException>(() => store.MaterializeAsync(application, "0001", Guid.NewGuid(), version, "../escape.pdf"));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private sealed class TestPaths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        {
            var resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && resolved != root)
                throw new InvalidOperationException("Outside test workspace.");
            return resolved;
        }
    }
}
