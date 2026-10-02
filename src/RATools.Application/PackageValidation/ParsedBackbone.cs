using System.Collections.Frozen;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed record ParsedXmlAttributeValue(string Name, string LocalName, string NamespaceUri, string Value,
    bool IsDefault, int Line, int Column);

public sealed class ParsedXmlElement
{
    internal ParsedXmlElement(string nodePath, string? parentPath, string name, string localName, string namespaceUri,
        IEnumerable<ParsedXmlAttributeValue> attributes, IEnumerable<string> children, string text, ValidationLocation location)
    {
        NodePath = nodePath;
        ParentPath = parentPath;
        Name = name;
        LocalName = localName;
        NamespaceUri = namespaceUri;
        Attributes = attributes.ToFrozenDictionary(attribute => attribute.Name, StringComparer.Ordinal);
        Children = Array.AsReadOnly(children.ToArray());
        Text = text;
        Location = location;
    }

    public string NodePath { get; }
    public string? ParentPath { get; }
    public string Name { get; }
    public string LocalName { get; }
    public string NamespaceUri { get; }
    public IReadOnlyDictionary<string, ParsedXmlAttributeValue> Attributes { get; }
    public IReadOnlyList<string> Children { get; }
    public string Text { get; }
    public ValidationLocation Location { get; }
    public string? Attribute(string name) => Attributes.GetValueOrDefault(name)?.Value;
    public string? Attribute(string localName, string namespaceUri) => Attributes.Values.FirstOrDefault(attribute =>
        attribute.LocalName == localName && attribute.NamespaceUri == namespaceUri)?.Value;
}

public sealed record ParsedXmlId(string Id, string NodePath, string ElementName, bool IsLeaf, ValidationLocation Location);
public sealed record ParsedXmlProcessingInstruction(string Target, string Data, ValidationLocation Location);

public sealed record ParsedCtdNode(string NodePath, string? ParentNodePath, string DefinitionKey, CtdNodeKind Kind,
    string? XmlId, string? Title, IReadOnlyDictionary<string, string?> IdentityAttributes, string? ContextKey,
    bool IdentityComplete, bool IdentityAmbiguous, ValidationLocation Location);

public sealed record ParsedPackageLeaf(string NodePath, string? ParentNodePath, string? Id, LeafAddress? Address,
    string? Title, string? Operation, string? ModifiedFile, string? Href, string? Checksum, string? ChecksumType,
    string? XmlLanguage, string? ContextKey, bool ContextComplete, bool IsRegionalReference, ValidationLocation Location);

public sealed class ParsedBackbone
{
    internal ParsedBackbone(string logicalPath, string profileSnapshotId, BackboneKind kind, bool readComplete, CheckStatus dtdStatus,
        string? documentTypeName, string? systemIdentifier, string? publicIdentifier,
        IEnumerable<ParsedXmlElement> elements, IEnumerable<ParsedCtdNode> nodes, IEnumerable<ParsedPackageLeaf> leaves,
        IEnumerable<ParsedXmlId> ids, IEnumerable<string> resolvedAssets, IEnumerable<ParsedXmlProcessingInstruction>? processingInstructions = null)
    {
        LogicalPath = logicalPath;
        ProfileSnapshotId = profileSnapshotId;
        Kind = kind;
        ReadComplete = readComplete;
        DtdStatus = dtdStatus;
        DocumentTypeName = documentTypeName;
        SystemIdentifier = systemIdentifier;
        PublicIdentifier = publicIdentifier;
        Elements = Array.AsReadOnly(elements.ToArray());
        Nodes = Array.AsReadOnly(nodes.ToArray());
        Leaves = Array.AsReadOnly(leaves.ToArray());
        IdIndex = ids.GroupBy(id => id.Id, StringComparer.Ordinal).ToFrozenDictionary(group => group.Key,
            group => (IReadOnlyList<ParsedXmlId>)Array.AsReadOnly(group.ToArray()), StringComparer.Ordinal);
        ResolvedAssets = Array.AsReadOnly(resolvedAssets.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        ProcessingInstructions = Array.AsReadOnly((processingInstructions ?? []).ToArray());
    }

    public string LogicalPath { get; }
    public string ProfileSnapshotId { get; }
    public BackboneKind Kind { get; }
    public bool ReadComplete { get; }
    public CheckStatus DtdStatus { get; }
    public string? DocumentTypeName { get; }
    public string? SystemIdentifier { get; }
    public string? PublicIdentifier { get; }
    public IReadOnlyList<ParsedXmlElement> Elements { get; }
    public IReadOnlyList<ParsedCtdNode> Nodes { get; }
    public IReadOnlyList<ParsedPackageLeaf> Leaves { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<ParsedXmlId>> IdIndex { get; }
    public IReadOnlyList<string> ResolvedAssets { get; }
    public IReadOnlyList<ParsedXmlProcessingInstruction> ProcessingInstructions { get; }
}

public sealed class PackageXmlInspection
{
    internal PackageXmlInspection(string inputDigest, string profileSnapshotId, string assetManifestDigest, string limitsDigest,
        IEnumerable<ParsedBackbone> backbones, IEnumerable<RuleCheckResult> checks)
    {
        InputDigest = inputDigest;
        ProfileSnapshotId = profileSnapshotId;
        AssetManifestDigest = assetManifestDigest;
        LimitsDigest = limitsDigest;
        Backbones = Array.AsReadOnly(backbones.ToArray());
        Checks = Array.AsReadOnly(checks.ToArray());
    }

    public string InputDigest { get; }
    public int SchemaVersion { get; } = 1;
    public string EngineVersion { get; } = "package-xml-inspector-v1";
    public string RulesDigest { get; } = PackageValidationCatalog.Current.Digest;
    public string ProfileSnapshotId { get; }
    public string AssetManifestDigest { get; }
    public string LimitsDigest { get; }
    public IReadOnlyList<ParsedBackbone> Backbones { get; }
    public IReadOnlyList<RuleCheckResult> Checks { get; }
}
