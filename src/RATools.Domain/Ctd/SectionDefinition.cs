using System.Collections.Frozen;
using System.Xml;

namespace RATools.Domain.Ctd;

public enum CtdNodeKind { Standard, Extension }
public enum NodeExtensionPolicy { Forbidden, Allowed }
public enum SectionAttributeValueType { CData, XmlId, Enumeration }

public sealed class SectionAttributeDefinition
{
    public SectionAttributeDefinition(string name, SectionAttributeValueType valueType, bool required, bool identity,
        IEnumerable<string> allowedValues)
    {
        Name = XmlConvert.VerifyName(name);
        if (!Enum.IsDefined(valueType)) throw new ArgumentOutOfRangeException(nameof(valueType));
        ValueType = valueType;
        Required = required;
        Identity = identity;
        AllowedValues = Array.AsReadOnly(allowedValues.ToArray());
        if ((valueType == SectionAttributeValueType.Enumeration) != (AllowedValues.Count > 0))
            throw new ArgumentException("Only enumeration attributes have a nonempty allowed-values list.", nameof(allowedValues));
    }

    public string Name { get; }
    public SectionAttributeValueType ValueType { get; }
    public bool Required { get; }
    public bool Identity { get; }
    public IReadOnlyList<string> AllowedValues { get; }
}

public sealed record SectionChildDefinition
{
    public SectionChildDefinition(string definitionKey, int minimum, int? maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        if (maximum is < 1 || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
        DefinitionKey = definitionKey;
        Minimum = minimum;
        Maximum = maximum;
    }

    public string DefinitionKey { get; }
    public int Minimum { get; }
    public int? Maximum { get; }
}

public sealed record NodeSchemaIssue(string Code, string FieldPath, string Message);

public sealed class SectionDefinition
{
    private readonly FrozenDictionary<string, SectionAttributeDefinition> _attributesByName;

    public SectionDefinition(string definitionKey, string elementName, string? sectionPath, string? parentDefinitionKey,
        bool repeatable, CtdNodeKind kind, bool allowsLeaves, NodeExtensionPolicy extensionPolicy,
        IEnumerable<SectionAttributeDefinition> attributes, IEnumerable<SectionChildDefinition> children)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(extensionPolicy)) throw new ArgumentOutOfRangeException(nameof(extensionPolicy));
        if (kind == CtdNodeKind.Standard) ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);
        if (kind == CtdNodeKind.Extension && (sectionPath is not null || parentDefinitionKey is not null))
            throw new ArgumentException("An extension inherits its section from its instance parent.", nameof(sectionPath));
        DefinitionKey = definitionKey;
        ElementName = XmlConvert.VerifyName(elementName);
        SectionPath = sectionPath;
        ParentDefinitionKey = parentDefinitionKey;
        Repeatable = repeatable;
        Kind = kind;
        AllowsLeaves = allowsLeaves;
        ExtensionPolicy = extensionPolicy;
        Attributes = Array.AsReadOnly(attributes.ToArray());
        Children = Array.AsReadOnly(children.ToArray());
        _attributesByName = Attributes.ToFrozenDictionary(attribute => attribute.Name, StringComparer.Ordinal);
        if (Children.Select(child => child.DefinitionKey).Distinct(StringComparer.Ordinal).Count() != Children.Count)
            throw new ArgumentException("Child definitions must be unique.", nameof(children));
        if (Children.Count > 0 && extensionPolicy == NodeExtensionPolicy.Allowed)
            throw new ArgumentException("Extensions cannot be mixed with defined child sections.", nameof(extensionPolicy));
        if (kind == CtdNodeKind.Extension && (!repeatable || !allowsLeaves || extensionPolicy != NodeExtensionPolicy.Allowed))
            throw new ArgumentException("Extension definitions must permit their repeatable leaf/extension content.", nameof(kind));
    }

    public string DefinitionKey { get; }
    public string ElementName { get; }
    public string? SectionPath { get; }
    public string? ParentDefinitionKey { get; }
    public bool Repeatable { get; }
    public CtdNodeKind Kind { get; }
    public bool AllowsLeaves { get; }
    public NodeExtensionPolicy ExtensionPolicy { get; }
    public IReadOnlyList<SectionAttributeDefinition> Attributes { get; }
    public IReadOnlyList<SectionChildDefinition> Children { get; }

    public IReadOnlyList<NodeSchemaIssue> ValidateAttributes(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var issues = new List<NodeSchemaIssue>();
        // Use ordinal keys even if a caller supplied a case-insensitive dictionary.
        var exact = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var attribute in Attributes.Where(attribute => attribute.Required))
        {
            if (!exact.TryGetValue(attribute.Name, out var value) || string.IsNullOrWhiteSpace(value))
                issues.Add(new("RequiredNodeAttributeMissing", $"attributes.{attribute.Name}",
                    $"'{attribute.Name}' requires a nonblank value for '{DefinitionKey}'."));
        }
        foreach (var (name, value) in exact.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var field = $"attributes.{name}";
            if (!_attributesByName.TryGetValue(name, out var definition))
            {
                issues.Add(new("UnknownNodeAttribute", field, $"'{name}' is not allowed on '{DefinitionKey}'."));
                continue;
            }
            if (value is null)
            {
                issues.Add(new("InvalidNodeAttributeValue", field, "An attribute value must be an XML string."));
                continue;
            }
            try
            {
                XmlConvert.VerifyXmlChars(value);
                if (definition.ValueType == SectionAttributeValueType.XmlId) XmlConvert.VerifyName(value);
            }
            catch (XmlException)
            {
                issues.Add(new("InvalidNodeAttributeValue", field, $"'{name}' is not a valid {definition.ValueType} value."));
            }
            if (definition.ValueType == SectionAttributeValueType.Enumeration &&
                !definition.AllowedValues.Contains(value, StringComparer.Ordinal))
                issues.Add(new("InvalidNodeAttributeChoice", field, $"'{name}' must use a schema-declared value."));
        }
        return issues.AsReadOnly();
    }

    public IReadOnlyList<NodeSchemaIssue> ValidateContent(IReadOnlyList<string> elementNames)
    {
        ArgumentNullException.ThrowIfNull(elementNames);
        var issues = new List<NodeSchemaIssue>();
        if (Kind == CtdNodeKind.Extension)
        {
            if (elementNames.Count < 2 || elementNames[0] != "title")
                issues.Add(new("InvalidExtensionContent", "children", "An extension requires a title followed by at least one leaf or extension."));
            foreach (var (name, index) in elementNames.Select((name, index) => (name, index)).Skip(1))
                if (name != "leaf" && name != "node-extension")
                    issues.Add(new("InvalidNodeChild", $"children[{index}]", $"'{name}' is not extension content."));
            return issues.AsReadOnly();
        }

        var counts = new int[Children.Count];
        var lastSlot = -1;
        for (var index = 0; index < elementNames.Count; index++)
        {
            var name = elementNames[index];
            if (name == "leaf" && AllowsLeaves)
            {
                if (lastSlot >= 0)
                    issues.Add(new("InvalidNodeContentOrder", $"children[{index}]", "Leaves must precede defined child sections."));
                continue;
            }
            if (name == "node-extension" && ExtensionPolicy == NodeExtensionPolicy.Allowed) continue;
            var slot = -1;
            for (var child = 0; child < Children.Count; child++)
                if (Children[child].DefinitionKey == name) { slot = child; break; }
            if (slot < 0)
            {
                issues.Add(new("InvalidNodeChild", $"children[{index}]", $"'{name}' is not allowed under '{DefinitionKey}'."));
                continue;
            }
            if (slot < lastSlot)
                issues.Add(new("InvalidNodeContentOrder", $"children[{index}]", "Defined children must follow schema order."));
            lastSlot = Math.Max(lastSlot, slot);
            counts[slot]++;
            if (counts[slot] > Children[slot].Maximum)
                issues.Add(new("NodeCardinalityExceeded", $"children[{index}]", $"'{name}' exceeds its permitted occurrences."));
        }
        for (var slot = 0; slot < Children.Count; slot++)
            if (counts[slot] < Children[slot].Minimum)
                issues.Add(new("RequiredNodeChildMissing", "children", $"'{Children[slot].DefinitionKey}' is required."));
        return issues.AsReadOnly();
    }
}
