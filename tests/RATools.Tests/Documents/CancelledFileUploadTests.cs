using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Storage;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Documents;

public sealed class CancelledFileUploadTests
{
    [Fact]
    public async Task CancellationAfterPartialReadRemovesOnlyTheNewFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ratools-cancel-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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
