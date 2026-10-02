using System.Net;
using System.Xml;

namespace RATools.Application.PackageValidation;

internal sealed class PackageXmlResolver : XmlResolver
{
    private readonly PackageXmlAssets _assets;
    private readonly Dictionary<string, string> _allowed;
    private readonly Uri _documentUri;
    public HashSet<string> ResolvedAssets { get; } = new(StringComparer.Ordinal);

    public PackageXmlResolver(PackageXmlAssets assets, PackageXmlBackboneProfile profile, string logicalPath)
    {
        _assets = assets;
        _documentUri = DocumentUri(logicalPath);
        var sequence = logicalPath.Split('/')[0];
        _allowed = profile.AllowedAssetIds.ToDictionary(id => DocumentUri(sequence + "/" + assets.Assets[id].LogicalPath).AbsoluteUri,
            id => id, StringComparer.Ordinal);
    }

    public override ICredentials? Credentials { set { } }

    public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
    {
        // XmlReader may request normalization of the controlled document base.
        if (baseUri is null && relativeUri == _documentUri.AbsoluteUri) return _documentUri;
        // For parameter entities, XmlReader first normalizes the base of an
        // already-served trusted module. This is not a new external request.
        if (baseUri is null && relativeUri is not null && _allowed.TryGetValue(relativeUri, out var existingAsset) && ResolvedAssets.Contains(existingAsset))
            return new Uri(relativeUri);
        if (string.IsNullOrEmpty(relativeUri) || Uri.TryCreate(relativeUri, UriKind.Absolute, out _) ||
            baseUri is null || baseUri.Scheme != "https" || baseUri.Host != "package.invalid")
            throw new UntrustedXmlResourceException($"Untrusted XML resource identifier '{relativeUri}' with base '{baseUri}'.");
        PackageReference resolved;
        try { resolved = PackageLogicalPath.ResolveReference(Uri.UnescapeDataString(baseUri.AbsolutePath.TrimStart('/')), relativeUri); }
        catch (ArgumentException exception) { throw new UntrustedXmlResourceException(exception.Message); }
        if (resolved.LogicalPath is null || resolved.Query is not null || resolved.Fragment is not null)
            throw new UntrustedXmlResourceException("XML resources cannot have external URLs, queries or fragments.");
        var uri = DocumentUri(resolved.LogicalPath);
        if (!_allowed.ContainsKey(uri.AbsoluteUri)) throw new UntrustedXmlResourceException("The XML resource does not match an exact pinned profile asset path.");
        return uri;
    }

    public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        if (!_allowed.TryGetValue(absoluteUri.AbsoluteUri, out var assetId))
            throw new UntrustedXmlResourceException("The requested XML resource is outside the pinned profile assets.");
        ResolvedAssets.Add(assetId);
        return _assets.Open(assetId);
    }

    public override Task<object> GetEntityAsync(Uri absoluteUri, string? role, Type? ofObjectToReturn) => Task.FromResult(GetEntity(absoluteUri, role, ofObjectToReturn));

    public static Uri DocumentUri(string logicalPath) => new("https://package.invalid/" + string.Join('/', logicalPath.Split('/').Select(Uri.EscapeDataString)));
}

internal sealed class UntrustedXmlResourceException(string message) : XmlException(message);
