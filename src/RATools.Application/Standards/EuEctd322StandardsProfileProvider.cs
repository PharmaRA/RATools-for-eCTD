using System.Security.Cryptography;
using RATools.Application.Applications.EctdTemplates;

namespace RATools.Application.Standards;

public sealed class EuEctd322StandardsProfileProvider : IStandardsProfileProvider
{
    private const string EmaEctdUrl = "https://esubmission.ema.europa.eu/ectd/";
    private const string IchSpecificationUrl = "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf";
    private readonly string _assetRootPath;

    public EuEctd322StandardsProfileProvider(string? assetRootPath = null)
    {
        _assetRootPath = string.IsNullOrWhiteSpace(assetRootPath)
            ? AppContext.BaseDirectory
            : assetRootPath;
    }

    public StandardsProfile GetProfile(string templateKey)
    {
        if (!string.Equals(templateKey, EctdTemplateRegistry.EuTemplateKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new StandardsProfileNotFoundException($"Unsupported standards profile '{templateKey}'.");
        }

        return new StandardsProfile(
            EctdTemplateRegistry.EuTemplateKey,
            "EU eCTD v3.2.2 + EU Regional M1",
            "European Medicines Agency",
            "EU",
            "3.2.2",
            "EU M1",
            "EU",
            "EU",
            [EmaEctdUrl, IchSpecificationUrl],
            [
                BuildAsset(
                    "ich-ectd-3-2-dtd",
                    "ICH eCTD DTD",
                    "DTD",
                    "3.2.2",
                    "reference/dtd/ich-ectd-3-2.dtd",
                    IchSpecificationUrl,
                    new DateOnly(2008, 7, 16)),
                BuildAsset(
                    "eu-regional-dtd",
                    "EU Regional DTD",
                    "DTD",
                    "EU M1",
                    "reference/dtd/eu-regional.dtd",
                    EmaEctdUrl,
                    null)
            ],
            BackboneXmlProfiles.EuEctd322Regional);
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
