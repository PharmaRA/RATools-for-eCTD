using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Storage;
using RATools.Infrastructure.Storage;
using RATools.Tests.PackageValidation;

namespace RATools.Tests.Documents;

public sealed class CancelledFileUploadTests
{
    [Fact]
    public async Task CancellationAfterPartialReadRetainsOnlyTheNewFile()
    {
        var directory = RetainedPackageTestFiles.Create();
        var existing = Path.Combine(directory, "existing.pdf");
        try
        {
            await File.WriteAllTextAsync(existing, "existing content");
            using var cancellation = new CancellationTokenSource();
            await using var source = new CancelAfterFirstRead(cancellation);
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = directory }));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.SaveAsync(new FileUploadRequest
            {
                FileName = "new.pdf", MediaType = "application/pdf", Content = source
            }, cancellation.Token));
            Assert.Equal(existing, Assert.Single(Directory.GetFiles(directory)));
            Assert.Equal("existing content", await File.ReadAllTextAsync(existing));
            var retained = Assert.Single(Directory.GetFiles(Path.Combine(directory, "_recycle")));
            Assert.Equal(new byte[10], await File.ReadAllBytesAsync(retained));
        }
        finally
        {
            RetainedPackageTestFiles.RetainDirectory(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceFailurePreservesOriginalErrorAndWrittenBytes(bool blockRecycle)
    {
        var directory = RetainedPackageTestFiles.Create();
        try
        {
            var recycle = Path.Combine(directory, "_recycle");
            if (blockRecycle) await File.WriteAllTextAsync(recycle, "retention blocked");
            var failure = new IOException("controlled source failure");
            await using var source = new FailingSource(failure);
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = directory }));
            var actual = await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(new FileUploadRequest
            {
                FileName = "new.pdf", MediaType = "application/pdf", Content = source, PreserveFileName = true
            }));
            Assert.Same(failure, actual);
            if (blockRecycle)
            {
                Assert.IsAssignableFrom<IOException>(actual.Data["UploadRetentionFailure"]);
                Assert.Equal("retention blocked", await File.ReadAllTextAsync(recycle));
                Assert.Equal("prefix"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(directory, "new.pdf")));
            }
            else
            {
                Assert.False(File.Exists(Path.Combine(directory, "new.pdf")));
                var retained = Assert.Single(Directory.GetFiles(recycle));
                Assert.Equal("prefix"u8.ToArray(), await File.ReadAllBytesAsync(retained));
            }
        }
        finally { RetainedPackageTestFiles.RetainDirectory(directory); }
    }

    [Fact]
    public async Task ExistingDestinationIsNeverMovedOnCreateFailure()
    {
        var directory = RetainedPackageTestFiles.Create();
        try
        {
            var path = Path.Combine(directory, "existing.pdf");
            await File.WriteAllTextAsync(path, "original");
            await using var source = new MemoryStream("replacement"u8.ToArray());
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = directory }));
            await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(new FileUploadRequest
            {
                FileName = "existing.pdf", MediaType = "application/pdf", Content = source, PreserveFileName = true
            }));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.False(Directory.Exists(Path.Combine(directory, "_recycle")));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(directory); }
    }

    [Fact]
    public async Task SuccessfulUploadKeepsOriginalLocationAndAccurateDigests()
    {
        var directory = RetainedPackageTestFiles.Create();
        try
        {
            var bytes = "successful payload"u8.ToArray();
            await using var source = new MemoryStream(bytes);
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = directory }));
            var result = await storage.SaveAsync(new FileUploadRequest
            {
                FileName = "new.pdf", MediaType = "application/pdf", Content = source, PreserveFileName = true
            });
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(directory, "new.pdf")));
            Assert.False(Directory.Exists(Path.Combine(directory, "_recycle")));
            Assert.Equal(bytes.Length, result.FileSize);
            Assert.Equal(Path.Combine(directory, "new.pdf"), result.StoragePath);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), result.Sha256);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes)).ToLowerInvariant(), result.Md5);
        }
        finally { RetainedPackageTestFiles.RetainDirectory(directory); }
    }

    private sealed class FailingSource(IOException failure) : MemoryStream("prefix"u8.ToArray())
    {
        private bool read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (read) throw failure;
            read = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class CancelAfterFirstRead(CancellationTokenSource cancellation) : MemoryStream(new byte[100])
    {
        private bool read;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (read)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            read = true;
            return base.ReadAsync(buffer[..10], cancellationToken);
        }
    }
}
