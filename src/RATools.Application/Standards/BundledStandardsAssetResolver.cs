using System.Security.Cryptography;

namespace RATools.Application.Standards;

internal sealed class BundledStandardsAssetResolver
{
    private readonly string _assetRootPath;

    public BundledStandardsAssetResolver(string? assetRootPath = null)
    {
        _assetRootPath = string.IsNullOrWhiteSpace(assetRootPath)
            ? AppContext.BaseDirectory
            : assetRootPath;
    }

    public StandardsAsset Build(
        string key,
        string displayName,
        string category,
        string version,
        string localRelativePath,
        string sourceUrl,
        DateOnly? supportedFrom)
    {
        var path = ResolveLocalAssetPath(localRelativePath);
        if (!File.Exists(path))
        {
            throw new StandardsAssetMissingException($"Bundled standards asset '{localRelativePath}' was not found at '{path}'.");
        }

        return new StandardsAsset(key, displayName, category, version, localRelativePath, sourceUrl, supportedFrom, ComputeSha256(path));
    }

    private string ResolveLocalAssetPath(string localRelativePath)
        => Path.Combine(_assetRootPath, localRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }
}
