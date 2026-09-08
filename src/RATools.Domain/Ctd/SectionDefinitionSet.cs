using System.Collections.Frozen;

namespace RATools.Domain.Ctd;

public sealed class SectionDefinitionSet
{
    public SectionDefinitionSet(string version, string identityComparisonVersion, string sourceSha256,
        IEnumerable<SectionDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityComparisonVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        Version = version;
        IdentityComparisonVersion = identityComparisonVersion;
        SourceSha256 = sourceSha256;
        Definitions = definitions.ToFrozenDictionary(definition => definition.DefinitionKey, StringComparer.Ordinal);
        Roots = Array.AsReadOnly(Definitions.Values.Where(definition =>
            definition.ParentDefinitionKey is null && definition.Kind == CtdNodeKind.Standard)
            .OrderBy(definition => definition.DefinitionKey, StringComparer.Ordinal).ToArray());

        foreach (var definition in Definitions.Values)
        {
            foreach (var child in definition.Children)
            {
                if (!Definitions.TryGetValue(child.DefinitionKey, out var childDefinition) ||
                    childDefinition.ParentDefinitionKey != definition.DefinitionKey ||
                    childDefinition.Repeatable != (child.Maximum is null or > 1))
                    throw new ArgumentException($"Inconsistent parent/cardinality for '{child.DefinitionKey}'.", nameof(definitions));
            }
            var visited = new HashSet<string>(StringComparer.Ordinal) { definition.DefinitionKey };
            var cursor = definition;
            while (cursor.ParentDefinitionKey is { } parentKey)
            {
                if (!visited.Add(parentKey) || !Definitions.TryGetValue(parentKey, out var parent) ||
                    parent.Children.All(child => child.DefinitionKey != cursor.DefinitionKey))
                    throw new ArgumentException($"Invalid parent chain for '{definition.DefinitionKey}'.", nameof(definitions));
                cursor = parent;
            }
        }
    }

    public string Version { get; }
    public string IdentityComparisonVersion { get; }
    public string SourceSha256 { get; }
    public IReadOnlyDictionary<string, SectionDefinition> Definitions { get; }
    public IReadOnlyList<SectionDefinition> Roots { get; }

    public SectionDefinition Get(string definitionKey) => Definitions.TryGetValue(definitionKey, out var definition)
        ? definition : throw new ArgumentException($"Unknown section definition '{definitionKey}'.", nameof(definitionKey));
}
