using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Domain.Ctd;

namespace RATools.Application.Ctd;

public static class IchSectionDefinitions
{
    public const string Version = "ich-3.2.2-nodes-v1";
    private static readonly Lazy<SectionDefinitionSet> Snapshot = new(Load);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static SectionDefinitionSet Current => Snapshot.Value;

    private static SectionDefinitionSet Load()
    {
        using var stream = typeof(IchSectionDefinitions).Assembly.GetManifestResourceStream("RATools.IchNodeSchema")
            ?? throw new InvalidOperationException("The pinned ICH node schema is missing.");
        var snapshot = JsonSerializer.Deserialize<SnapshotFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The pinned ICH node schema is empty.");
        if (snapshot.SchemaVersion != 1 || snapshot.DefinitionVersion != Version || snapshot.DtdVersion != "3.2" ||
            snapshot.IdentityComparisonVersion != "exact-xml-values-v1")
            throw new InvalidOperationException("Unsupported ICH node schema version.");
        return new SectionDefinitionSet(snapshot.DefinitionVersion, snapshot.IdentityComparisonVersion, snapshot.DtdSha256,
            snapshot.Definitions.Select(definition => new SectionDefinition(definition.DefinitionKey, definition.ElementName,
                definition.SectionPath, definition.ParentDefinitionKey, definition.Repeatable, definition.Kind,
                definition.AllowsLeaves, definition.ExtensionPolicy,
                definition.Attributes.Select(attribute => new SectionAttributeDefinition(attribute.Name, attribute.ValueType,
                    attribute.Required, attribute.Identity, attribute.AllowedValues)), definition.Children)));
    }

    private sealed record SnapshotFile(int SchemaVersion, string DefinitionVersion, string DtdVersion, string DtdSha256,
        string IdentityComparisonVersion, DefinitionFile[] Definitions);
    private sealed record DefinitionFile(string DefinitionKey, string ElementName, string? SectionPath,
        string? ParentDefinitionKey, bool Repeatable, CtdNodeKind Kind, bool AllowsLeaves, NodeExtensionPolicy ExtensionPolicy,
        AttributeFile[] Attributes, SectionChildDefinition[] Children);
    private sealed record AttributeFile(string Name, SectionAttributeValueType ValueType, bool Required, bool Identity,
        string[] AllowedValues);
}
