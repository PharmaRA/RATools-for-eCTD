using System.Security.Cryptography;

namespace RATools.Application.Applications;

internal sealed class ImportFileHashCache
{
    private const int BufferSize = 81920;

    private readonly Dictionary<string, ImportFileHashes> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, Stream> _openRead;

    public ImportFileHashCache()
        : this(File.OpenRead)
    {
    }

    internal ImportFileHashCache(Func<string, Stream> openRead)
    {
        _openRead = openRead;
    }

    public async Task<ImportFileHashes> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(path);
        if (_hashes.TryGetValue(normalizedPath, out var cached))
        {
            return cached;
        }

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = _openRead(normalizedPath);
        var buffer = new byte[BufferSize];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            md5.AppendData(buffer, 0, read);
            sha256.AppendData(buffer, 0, read);
        }

        var hashes = new ImportFileHashes(
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant());
        _hashes[normalizedPath] = hashes;
        return hashes;
    }
}

internal readonly record struct ImportFileHashes(string Md5, string Sha256);
