namespace RATools.Application.Publishing.PackageModel;

public static class EctdLeafHref
{
    // Href is the physical delivery path relative to the sequence. XML hrefs are
    // relative to the writing backbone and may retain their original URI spelling.
    public static string FromBackbone(EctdLeaf leaf, string backboneRelativePath)
    {
        var root = new Uri("https://ectd.invalid/sequence/");
        var source = new Uri(root, EncodePath(backboneRelativePath));
        var target = new Uri(root, EncodePath(leaf.Href));
        if (leaf.ImportedSource?.Href is { } original && original == original.Trim() && !original.Contains('\\') &&
            !original.StartsWith('/') && !Uri.TryCreate(original, UriKind.Absolute, out _) && Uri.TryCreate(source, original, out var imported) &&
            imported == target && imported.Query.Length == 0 && imported.Fragment.Length == 0)
            return original;
        return new Uri(source, ".").MakeRelativeUri(target).ToString();
    }

    private static string EncodePath(string path) => string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
}
