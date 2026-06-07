using System.Security.Cryptography;
using RATools.Application.Applications.EctdTemplates;

namespace RATools.Application.Standards;

public sealed class FdaEctd322StandardsProfileProvider : IStandardsProfileProvider
{
    private const string StandardsPageUrl = "https://www.fda.gov/drugs/electronic-regulatory-submission-and-review/ectd-submission-standards-ectd-v322-and-regional-m1";
    private const string EctdOverviewUrl = "https://www.fda.gov/ectd";
    private const string IchSpecificationUrl = "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf";

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

    private static StandardsAsset BuildAsset(
        string key,
        string displayName,
        string category,
        string version,
        string localRelativePath,
        string sourceUrl,
        DateOnly? supportedFrom)
    {
        var path = ResolveLocalAssetPath(localRelativePath);
        var sha256 = File.Exists(path) ? ComputeSha256(path) : string.Empty;
        return new StandardsAsset(key, displayName, category, version, localRelativePath, sourceUrl, supportedFrom, sha256);
    }

    private static string ResolveLocalAssetPath(string localRelativePath)
        => Path.Combine(AppContext.BaseDirectory, localRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }
}
