using System.Collections.Frozen;
using System.Text.Json;
using System.Xml;
using RATools.Application.Ctd;
using RATools.Domain.Common;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class PackageXmlIdentityInspector
{
    public static ParsedBackbone Inspect(ReadXmlDocument document, Guid applicationId, string sequence, string logicalPath,
        string profileSnapshotId, BackboneKind kind, string dtdRuleId, XmlInspectionFindings findings, CancellationToken cancellationToken)
    {
        var elements = document.Elements;
        var nodes = new List<ParsedCtdNode>();
        var leaves = new List<ParsedPackageLeaf>();
        var ids = new List<ParsedXmlId>();
        if (!document.Complete) return Build();
        var byPath = elements.ToDictionary(element => element.NodePath, StringComparer.Ordinal);
        var schema = IchSectionDefinitions.Current;
        var nodeMap = new Dictionary<string, ParsedCtdNode>(StringComparer.Ordinal);

        foreach (var element in elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in element.Attributes.Values.Where(attribute => attribute.Name is "ID" or "xml:id"))
            {
                var location = AttributeLocation(element, attribute.Name);
                var isLeaf = element.Name == "leaf" && element.NamespaceUri.Length == 0;
                if (isLeaf) location = location with { LeafId = attribute.Value };
                ids.Add(new(attribute.Value, element.NodePath, element.Name, isLeaf, location));
                try { XmlConvert.VerifyName(attribute.Value); }
                catch (Exception exception) when (exception is XmlException or ArgumentException)
                {
                    findings.Add(new("XML-IDS", CheckStatus.Fail, ValidationSeverity.Error, "INVALID_XML_ID", "XML ID is not a valid XML Name.", location, Actual: attribute.Value));
                }
            }
            if (kind != BackboneKind.Ich || element.NamespaceUri.Length != 0 || !schema.Definitions.TryGetValue(element.Name, out var definition)) continue;
            var parent = element.ParentPath is { } parentPath ? nodeMap.GetValueOrDefault(parentPath) : null;
            var expectedParent = definition.ParentDefinitionKey;
            var invalidParent = definition.Kind == CtdNodeKind.Standard && (expectedParent is not null ? parent?.DefinitionKey != expectedParent : element.ParentPath != elements[0].NodePath) ||
                definition.Kind == CtdNodeKind.Extension && (parent is null || schema.Get(parent.DefinitionKey).ExtensionPolicy != NodeExtensionPolicy.Allowed);
            if (invalidParent)
                findings.Add(new(definition.Kind == CtdNodeKind.Extension ? "NODE-EXTENSIONS" : "NODE-CONTEXT", CheckStatus.Fail, ValidationSeverity.Error, "INVALID_NODE_PARENT",
                    "The CTD instance does not have its schema-declared parent.", element.Location, expectedParent, parent?.DefinitionKey));

            var attributes = element.Attributes.Values.Where(attribute => attribute.NamespaceUri != "http://www.w3.org/2000/xmlns/")
                .ToDictionary(attribute => attribute.Name, attribute => attribute.Value, StringComparer.Ordinal);
            var attributeIssues = definition.ValidateAttributes(attributes);
            foreach (var issue in attributeIssues)
                findings.Add(new("NODE-CONTEXT", CheckStatus.Fail, ValidationSeverity.Error,
                    issue.Code == "RequiredNodeAttributeMissing" ? "MISSING_REQUIRED_NODE_ATTRIBUTE" : issue.Code,
                    issue.Message, AttributeLocation(element, issue.FieldPath.Replace("attributes.", "", StringComparison.Ordinal))));
            foreach (var issue in definition.ValidateContent(element.Children.Select(path => byPath[path].Name).ToArray()))
                findings.Add(new(definition.Kind == CtdNodeKind.Extension || issue.Code == "InvalidExtensionContent" ? "NODE-EXTENSIONS" : "NODE-CONTEXT",
                    CheckStatus.Fail, ValidationSeverity.Error, issue.Code, issue.Message, element.Location with { FieldPath = issue.FieldPath }));

            var identity = definition.Attributes.Where(attribute => attribute.Identity)
                .ToFrozenDictionary(attribute => attribute.Name, attribute => element.Attribute(attribute.Name), StringComparer.Ordinal);
            var complete = attributeIssues.Count == 0 && !invalidParent && (parent?.IdentityComplete ?? true);
            var xmlId = element.Attribute("ID");
            if (definition.Kind == CtdNodeKind.Extension && string.IsNullOrWhiteSpace(xmlId))
            {
                complete = false;
                findings.Add(new("NODE-CONTEXT", CheckStatus.NotEvaluated, ValidationSeverity.Warning, "EXTENSION_IDENTITY_UNRESOLVED",
                    "This valid optional-ID extension has no explicit XML identity; its title is not a stable historical identity.", element.Location));
            }
            var key = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
            {
                definitionKey = definition.DefinitionKey, parentContext = parent?.ContextKey,
                identityAttributes = identity, extensionXmlId = definition.Kind == CtdNodeKind.Extension ? xmlId : null
            }));
            var node = new ParsedCtdNode(element.NodePath, parent?.NodePath, definition.DefinitionKey, definition.Kind, xmlId,
                Title(element, byPath), identity, key, complete, false, element.Location);
            nodes.Add(node);
            nodeMap.Add(node.NodePath, node);
        }

        foreach (var group in ids.GroupBy(id => id.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            foreach (var id in group)
                findings.Add(new("XML-IDS", CheckStatus.Fail, ValidationSeverity.Error, "DUPLICATE_XML_ID",
                    "The same XML ID occurs more than once in this backbone; fragment lookup is ambiguous.", id.Location, "One ID per XML document", id.Id));

        var ambiguous = nodes.GroupBy(node => (node.ParentNodePath, node.DefinitionKey, node.ContextKey))
            .Where(group => group.Count() > 1).SelectMany(group => group).Select(node => node.NodePath).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[index];
            var parent = node.ParentNodePath is { } path ? nodeMap[path] : null;
            var ownAmbiguity = ambiguous.Contains(node.NodePath);
            node = node with { IdentityAmbiguous = ownAmbiguity || parent?.IdentityAmbiguous == true,
                IdentityComplete = node.IdentityComplete && !ownAmbiguity && parent?.IdentityComplete != false };
            nodes[index] = node;
            nodeMap[node.NodePath] = node;
            if (ownAmbiguity)
                findings.Add(new("NODE-CONTEXT", CheckStatus.Fail, ValidationSeverity.Error, "AMBIGUOUS_NODE_IDENTITY",
                    "Sibling CTD instances have indistinguishable business identity; they remain separate and cannot be matched heuristically.", node.Location));
        }

        foreach (var element in elements.Where(element => element.Name == "leaf" && element.NamespaceUri.Length == 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = element.Attribute("ID");
            LeafAddress? address = null;
            try
            {
                if (id is not null) address = new(applicationId, sequence, logicalPath[(sequence.Length + 1)..], id);
            }
            catch (ArgumentException)
            {
                findings.Add(new("XML-IDS", CheckStatus.Fail, ValidationSeverity.Error, "INVALID_LEAF_ADDRESS", "The leaf cannot form an exact valid XML address.", element.Location));
            }
            catch (XmlException) { /* Invalid ID was already recorded above. */ }
            if (id is null)
                findings.Add(new("XML-IDS", CheckStatus.Fail, ValidationSeverity.Error, "MISSING_LEAF_ID", "A leaf requires an XML ID.", element.Location with { FieldPath = "@ID" }));
            var node = element.ParentPath is { } parentPath ? nodeMap.GetValueOrDefault(parentPath) : null;
            leaves.Add(new(element.NodePath, element.ParentPath, id, address, Title(element, byPath), element.Attribute("operation"),
                element.Attribute("modified-file"), element.Attribute("href", "http://www.w3c.org/1999/xlink"), element.Attribute("checksum"),
                element.Attribute("checksum-type"), element.Attribute("xml:lang"), node?.ContextKey,
                node?.IdentityComplete == true && !node.IdentityAmbiguous && address is not null,
                kind == BackboneKind.Ich && node?.DefinitionKey == "m1-administrative-information-and-prescribing-information",
                element.Location with { LeafId = id }));
        }
        return Build();

        ParsedBackbone Build() => new(logicalPath, profileSnapshotId, kind, document.Complete,
            findings.Items.Any(finding => finding.RuleId == dtdRuleId && finding.Location.LogicalPath == logicalPath && finding.CheckStatus == CheckStatus.Fail)
                ? CheckStatus.Fail : document.Complete ? CheckStatus.Pass : CheckStatus.NotEvaluated, document.DocumentTypeName,
            document.SystemId, document.PublicId, elements, nodes, leaves, ids, document.ResolvedAssets,
            document.Complete ? document.ProcessingInstructions : []);
    }

    private static string? Title(ParsedXmlElement element, Dictionary<string, ParsedXmlElement> byPath) => element.Children.Select(path => byPath[path])
        .FirstOrDefault(child => child.Name == "title" && child.NamespaceUri.Length == 0)?.Text;

    private static ValidationLocation AttributeLocation(ParsedXmlElement element, string name)
    {
        var attribute = element.Attributes.GetValueOrDefault(name);
        return element.Location with { FieldPath = "@" + name,
            Line = attribute?.Line is > 0 ? attribute.Line : element.Location.Line,
            Column = attribute?.Column is > 0 ? attribute.Column : element.Location.Column };
    }
}
