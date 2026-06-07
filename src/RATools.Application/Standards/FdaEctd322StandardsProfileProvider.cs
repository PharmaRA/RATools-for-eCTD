using System.Security.Cryptography;
using RATools.Application.Applications.EctdTemplates;

namespace RATools.Application.Standards;

public sealed class FdaEctd322StandardsProfileProvider : IStandardsProfileProvider
{
    private const string StandardsPageUrl = "https://www.fda.gov/drugs/electronic-regulatory-submission-and-review/ectd-submission-standards-ectd-v322-and-regional-m1";
    private const string EctdOverviewUrl = "https://www.fda.gov/ectd";
    private const string IchSpecificationUrl = "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf";
    private readonly string _assetRootPath;

    public FdaEctd322StandardsProfileProvider(string? assetRootPath = null)
    {
        _assetRootPath = string.IsNullOrWhiteSpace(assetRootPath)
            ? AppContext.BaseDirectory
            : assetRootPath;
    }

    public StandardsProfile GetProfile(string templateKey)
    {
        if (!string.Equals(templateKey, EctdTemplateRegistry.DefaultTemplateKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new StandardsProfileNotFoundException($"Unsupported standards profile '{templateKey}'.");
        }

        return new StandardsProfile(
            EctdTemplateRegistry.DefaultTemplateKey,
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "FDA CDER/CBER",
            "United States",
            "3.2.2",
            "3.3",
            "1.9",
            "4.5",
            [StandardsPageUrl, EctdOverviewUrl, IchSpecificationUrl],
            [
                BuildAsset(
                    "ich-ectd-3-2-dtd",
                    "ICH eCTD DTD",
                    "DTD",
                    "3.2.2",
                    "reference/dtd/ich-ectd-3-2.dtd",
                    StandardsPageUrl,
                    new DateOnly(2008, 7, 16)),
                BuildAsset(
                    "us-regional-v3-3-dtd",
                    "US Regional DTD",
                    "DTD",
                    "3.3",
                    "reference/dtd/us-regional-v3-3.dtd",
                    StandardsPageUrl,
                    new DateOnly(2015, 12, 1))
            ]);
    }

    private StandardsAsset BuildAsset(
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
