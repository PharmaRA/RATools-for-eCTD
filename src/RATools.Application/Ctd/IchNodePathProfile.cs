using System.Collections.Frozen;
using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Application.Ctd;

public static class IchNodePathProfile
{
    public const string Version = "ich-3.2.2-node-paths-v1";
    public static IReadOnlyDictionary<string, string> Paths { get; } = Load();

    private static FrozenDictionary<string, string> Load()
    {
        using var stream = typeof(IchNodePathProfile).Assembly.GetManifestResourceStream("RATools.IchNodePaths")
            ?? throw new InvalidOperationException("The pinned ICH path profile is missing.");
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.GetProperty("version").GetString() != Version)
            throw new InvalidOperationException("Unsupported node path profile version.");
        var paths = document.RootElement.GetProperty("paths").EnumerateObject()
            .ToFrozenDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
        foreach (var (key, path) in paths)
        {
            var definition = IchSectionDefinitions.Current.Get(key);
            if (!path.StartsWith(definition.SectionPath![..2], StringComparison.Ordinal))
                throw new InvalidOperationException("A node path is outside its module.");
            foreach (var segment in path.Split('/')) _ = PortablePathSegment.NormalizeAndValidate(segment, key);
            if (definition.ParentDefinitionKey is { } parent && path != paths[parent] && !path.StartsWith(paths[parent] + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("A node path is outside its parent's directory.");
        }
        return paths;
    }
}
