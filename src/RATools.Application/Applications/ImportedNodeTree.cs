using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using RATools.Application.Applications.Dtos;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;

namespace RATools.Application.Applications;

// Each parser owns a copy. Nothing (including ambiguity discovered in an older
// instance) becomes visible until the complete sequence has succeeded.
internal sealed class ImportedNodeTree(CtdNodeGraph previous, string sequenceNumber,
    ImportedLeafIndex history, List<ApplicationImportIssueDto> issues)
{
    private readonly Dictionary<XElement, Guid> _elements = new();
    private readonly List<(XElement Element, Guid Id, int Order)> _ordered = [];
    public CtdNodeGraph Graph { get; private set; } = new(previous.ApplicationId, previous.Definitions, previous.Nodes.Values);

    public void Read(XDocument xml, string sourcePath, CancellationToken cancellationToken)
    {
        if (xml.Root?.Name.LocalName != "ectd") throw new XmlException("The ICH backbone must have an ectd root element.");
        ReadChildren(xml.Root, null, sourcePath, cancellationToken);
    }

    public SequenceNode? ParentOf(XElement leaf) => leaf.Parent is { } parent && _elements.TryGetValue(parent, out var id)
        ? MakeNode(parent, id, _ordered.Single(item => item.Id == id).Order) : null;

    public IReadOnlyList<SequenceNode> Nodes => _ordered.Select(item => MakeNode(item.Element, item.Id, item.Order)).ToArray();

    private SequenceNode MakeNode(XElement element, Guid id, int order) => new(Graph, sequenceNumber, id,
        Attributes(element), element.Elements().FirstOrDefault(child => child.Name.LocalName == "title")?.Value, order);

    private void ReadChildren(XElement container, Guid? parentId, string sourcePath, CancellationToken cancellationToken)
    {
        var children = container.Elements().Where(element => element.Name.LocalName is not ("leaf" or "title")).ToArray();
        var candidates = new List<(XElement Element, CtdNodeInstance Node, int Order)>();
        foreach (var element in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var order = element.ElementsBeforeSelf().Count(sibling => sibling.Name.LocalName != "title");
            if (element.Name.NamespaceName is not ("" or "http://www.ich.org/ectd"))
                throw new XmlException($"Unknown node namespace '{element.Name.NamespaceName}'.");
            if (!IchSectionDefinitions.Current.Definitions.TryGetValue(element.Name.LocalName, out var definition))
            {
                if (parentId is not null || !IsAbbreviatedSection(element.Name.LocalName))
                    throw new XmlException($"Unknown child '{element.Name}' under '{container.Name}'.");
                // Earlier workspaces used abbreviated element names. Keep them
                // unbound, with provenance and a diagnostic at the leaf boundary.
                continue;
            }
            var attributes = Attributes(element);
            var titles = element.Elements().Where(child => child.Name.LocalName == "title").ToArray();
            if (titles.Length > 1 || titles.Length == 1 && (definition.Kind != CtdNodeKind.Extension || titles[0].ElementsBeforeSelf().Any()))
                throw new XmlException($"'{element.Name}' has an unsupported or misplaced title.");
            var attributeIssues = definition.ValidateAttributes(attributes);
            if (attributeIssues.Any(issue => issue.Code != "RequiredNodeAttributeMissing"))
                throw new XmlException(string.Join(" | ", attributeIssues.Select(issue => issue.Message)));
            var status = attributeIssues.Count == 0 ? CtdIdentityStatus.Resolved : CtdIdentityStatus.MissingMetadata;
            var instance = CtdNodeInstance.Rehydrate(Guid.NewGuid(), Graph.ApplicationId, parentId, Graph.Definitions,
                definition.DefinitionKey, attributes, status);
            candidates.Add((element, instance, order));
        }

        var extensionEvidence = candidates.Where(item => item.Node.Kind == CtdNodeKind.Extension)
            .ToDictionary(item => item.Element, item => ExtensionTarget(item.Element, parentId, sourcePath));
        foreach (var (element, candidate, order) in candidates)
        {
            var definition = Graph.Definitions.Get(candidate.DefinitionKey);
            var siblings = candidates.Where(item => item.Node.IdentityKey == candidate.IdentityKey).ToArray();
            var historical = Graph.Nodes.Values.Where(node => node.IdentityKey == candidate.IdentityKey).ToArray();
            var duplicate = siblings.Length > 1 || historical.Any(node => node.IdentityStatus == CtdIdentityStatus.Ambiguous);
            CtdNodeInstance instance;
            if (candidate.Kind == CtdNodeKind.Extension)
            {
                var evidence = extensionEvidence[element];
                instance = evidence is { } id && extensionEvidence.Values.Count(value => value == id) == 1
                    ? Graph.Get(id) : candidate;
                if (evidence is not null && instance == candidate)
                    Warn("NODE_IDENTITY_AMBIGUOUS", element, "Several extensions reference the same historical group; separate identities were retained.");
            }
            else if (!duplicate && historical.Length == 1 &&
                     (!definition.Repeatable || candidate.IdentityStatus == CtdIdentityStatus.Resolved) &&
                     historical[0].IdentityStatus == candidate.IdentityStatus)
                instance = historical[0];
            else
            {
                duplicate |= historical.Length > 0;
                if (duplicate)
                {
                    // Imports are not persisted yet. Mark every conflicting identity,
                    // including earlier sequences, before adding the separate sibling.
                    var replacements = Graph.Nodes.Values.Select(node => node.IdentityKey == candidate.IdentityKey
                        ? WithStatus(node, CtdIdentityStatus.Ambiguous) : node).ToArray();
                    Graph = new CtdNodeGraph(Graph.ApplicationId, Graph.Definitions, replacements);
                    instance = WithStatus(candidate, CtdIdentityStatus.Ambiguous);
                    Warn("NODE_IDENTITY_AMBIGUOUS", element, "Equal sibling attributes do not uniquely identify a business node; explicit mapping is required.");
                }
                else instance = candidate;
            }
            if (!Graph.Nodes.ContainsKey(instance.Id)) Graph.Add(instance);
            if (_ordered.Any(item => item.Id == instance.Id))
                throw new XmlException($"Node '{element.Name}' occurs more than once where the schema requires one instance.");
            _elements.Add(element, instance.Id);
            _ordered.Add((element, instance.Id, order));
            if (candidate.IdentityStatus == CtdIdentityStatus.MissingMetadata)
                Warn("NODE_METADATA_INCOMPLETE", element, "Required node attributes are missing; no values were inferred from directories or sponsor metadata.");
            if (instance.Kind == CtdNodeKind.Extension && string.IsNullOrWhiteSpace(element.Elements().FirstOrDefault(child => child.Name.LocalName == "title")?.Value))
                Warn("NODE_METADATA_INCOMPLETE", element, "The extension title is missing.");
            ReadChildren(element, instance.Id, sourcePath, cancellationToken);
        }
    }

    private Guid? ExtensionTarget(XElement element, Guid? parentId, string sourcePath)
    {
        var targets = new HashSet<Guid>();
        foreach (var leaf in element.Descendants().Where(child => child.Name.LocalName == "leaf"))
        {
            if (leaf.Attribute("operation")?.Value is not ("replace" or "append" or "delete")) continue;
            if (leaf.Attribute("modified-file")?.Value is not { } reference || !reference.Contains('#')) continue;
            var target = history.ResolveAddress(sourcePath, reference, sequenceNumber);
            if (target?.Placement.NodeInstanceId is not { } id) return null;
            var node = Graph.Get(id);
            while (node.ParentInstanceId != parentId && node.ParentInstanceId is { } ancestor) node = Graph.Get(ancestor);
            if (node.ParentInstanceId != parentId || node.Kind != CtdNodeKind.Extension || Graph.HasUnresolvedIdentity(node.Id)) return null;
            targets.Add(node.Id);
        }
        return targets.Count == 1 ? targets.Single() : null;
    }

    private CtdNodeInstance WithStatus(CtdNodeInstance node, CtdIdentityStatus status) => CtdNodeInstance.Rehydrate(
        node.Id, node.ApplicationId, node.ParentInstanceId, Graph.Definitions, node.DefinitionKey, node.IdentityAttributes, status);

    private void Warn(string code, XElement element, string message) => issues.Add(new("Warning", code, sequenceNumber,
        $"{string.Join('/', element.AncestorsAndSelf().Reverse().Select(node => $"{node.Name.LocalName}[{node.ElementsBeforeSelf(node.Name).Count() + 1}]"))}: {message}"));

    private static Dictionary<string, string> Attributes(XElement element) => element.Attributes()
        .Where(attribute => !attribute.IsNamespaceDeclaration).ToDictionary(attribute => CtdXmlAttributes.SchemaName(attribute.Name), attribute => attribute.Value, StringComparer.Ordinal);

    private static bool IsAbbreviatedSection(string name)
    {
        var parts = name.Split('-');
        return parts.Length > 1 && parts[0] is "m1" or "m2" or "m3" or "m4" or "m5" &&
            parts.Skip(1).All(part => part is "s" or "p" or "a" or "r" ||
                part.Length > 0 && part.All(character => character is >= '0' and <= '9'));
    }

    // Regional schema is deferred to P6. Conservative, exact XML context keeps
    // country/language/envelope dimensions apart without inventing identity fields.
    public static string UnboundContext(XElement leaf) => JsonSerializer.Serialize(leaf.Ancestors().Reverse()
        .Skip(1).Select(element => new
        {
            Name = element.Name.ToString(),
            Attributes = Attributes(element).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            Title = element.Elements().FirstOrDefault(child => child.Name.LocalName == "title")?.Value
        }).ToArray());
}
