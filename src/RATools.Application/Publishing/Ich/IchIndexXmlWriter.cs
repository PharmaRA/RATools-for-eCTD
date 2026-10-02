using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;

namespace RATools.Application.Publishing.Ich;

public sealed class IchIndexXmlWriter : IIchIndexXmlWriter
{
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";
    public IchIndexXmlWriteResult Write(EctdSequencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var xmlProfile = package.BackboneXml.Ich;
        XNamespace ectdNamespace = xmlProfile.Namespace;

        var root = new XElement(ectdNamespace + xmlProfile.RootElementName,
            new XAttribute(XNamespace.Xmlns + "ectd", ectdNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", xmlProfile.DtdVersion));
        ValidateLeaves(package);

        if (package.RegionalBackbones?.Any(reference => reference.Md5 is not null) == true)
        {
            var definition = IchSectionDefinitions.Current.Get("m1-administrative-information-and-prescribing-information");
            var container = new XElement(definition.ElementName);
            var sourceNode = package.Nodes?.SingleOrDefault(node => node.DefinitionKey == definition.DefinitionKey);
            if (sourceNode is not null)
            {
                if (sourceNode.DefinitionVersion != IchSectionDefinitions.Current.Version || sourceNode.ParentInstanceId is not null)
                    throw new EctdPackageNodeException("UnsupportedNodeSchema", "The ICH M1 container requires the supported root definition.", sourceNode.NodeInstanceId);
                var issues = definition.ValidateAttributes(sourceNode.Attributes);
                if (issues.Count > 0 || sourceNode.MetadataStatus != NodeMetadataStatus.Complete)
                    throw new EctdPackageNodeException("NodeMetadataIncomplete", "The ICH M1 container metadata is invalid.", sourceNode.NodeInstanceId);
                foreach (var attribute in definition.Attributes)
                    if (sourceNode.Attributes.TryGetValue(attribute.Name, out var value))
                        container.Add(new XAttribute(CtdXmlAttributes.XmlName(attribute.Name), value));
            }
            foreach (var reference in package.RegionalBackbones.Where(reference => reference.Md5 is not null).OrderBy(reference => reference.RelativePath, StringComparer.Ordinal))
            {
                if (reference.Operation == "delete")
                    throw new EctdPackageNodeException("RegionalProfileRequired", "Deleting a regional backbone requires an applicable regional output profile.");
                container.Add(new XElement("leaf", new XAttribute("ID", reference.LeafId), new XAttribute("operation", reference.Operation),
                    new XAttribute("checksum", reference.Md5!), new XAttribute("checksum-type", "md5"),
                    new XAttribute(XlinkNamespace + "href", reference.RelativePath),
                    reference.ModifiedFile is null ? null : new XAttribute("modified-file", reference.ModifiedFile),
                    new XElement("title", reference.Title)));
            }
            root.Add(container);
        }

        var tree = EctdPackageNodeTree.Create(package.ApplicationId, package.Nodes ?? [], package.IchBackboneLeaves,
            allowLegacyBinding: package.Nodes is null);
        var children = tree.Nodes.ToLookup(node => node.ParentInstanceId);
        var leaves = tree.Leaves.ToLookup(leaf => leaf.NodeInstanceId!.Value);
        foreach (var node in OrderedNodes(null)) root.Add(BuildNode(node));
        var xmlIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in root.DescendantsAndSelf().Attributes("ID"))
            if (!xmlIds.Add(id.Value)) throw new EctdPackageNodeException("DuplicateXmlId", $"Duplicate ICH XML ID '{id.Value}'.");

        IEnumerable<EctdPackageNode> OrderedNodes(EctdPackageNode? parent)
        {
            var order = parent is null ? IchSectionDefinitions.Current.Roots.Select(item => item.DefinitionKey).ToArray()
                : IchSectionDefinitions.Current.Get(parent.DefinitionKey).Children.Select(child => child.DefinitionKey).ToArray();
            return children[parent?.NodeInstanceId].OrderBy(node => Array.IndexOf(order, node.DefinitionKey))
                .ThenBy(node => node.SortOrder).ThenBy(node => node.NodeInstanceId);
        }

        XElement BuildNode(EctdPackageNode node)
        {
            var definition = IchSectionDefinitions.Current.Get(node.DefinitionKey);
            var element = new XElement(definition.ElementName);
            foreach (var attribute in definition.Attributes)
                if (node.Attributes.TryGetValue(attribute.Name, out var value))
                    element.Add(new XAttribute(CtdXmlAttributes.XmlName(attribute.Name), value));
            if (definition.Kind == CtdNodeKind.Extension) element.Add(new XElement("title", node.Title));
            if (definition.ExtensionPolicy == NodeExtensionPolicy.Allowed)
            {
                var entries = leaves[node.NodeInstanceId].Select(leaf => (leaf.SortOrder, Id: leaf.PlacementId, Element: BuildLeafElement(leaf)))
                    .Concat(children[node.NodeInstanceId].Select(child => (child.SortOrder, Id: child.NodeInstanceId, Element: BuildNode(child))))
                    .OrderBy(item => item.SortOrder).ThenBy(item => item.Id);
                element.Add(entries.Select(item => item.Element));
            }
            else
            {
                element.Add(leaves[node.NodeInstanceId].OrderBy(leaf => leaf.SortOrder).ThenBy(leaf => leaf.PlacementId).Select(BuildLeafElement));
                element.Add(OrderedNodes(node).Select(BuildNode));
            }
            var contentIssues = definition.ValidateContent(element.Elements().Select(child => child.Name.LocalName).ToArray());
            if (contentIssues.Count > 0)
                throw new EctdPackageNodeException("InvalidNodeContent", string.Join(" | ", contentIssues.Select(issue => issue.Message)), node.NodeInstanceId);
            return element;
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XDocumentType(xmlProfile.DocumentTypeName, null, xmlProfile.DtdSystemId, null),
            root);

        return new IchIndexXmlWriteResult("index.xml", document, document.ToString(SaveOptions.DisableFormatting));
    }

    private static void ValidateLeaves(EctdSequencePackage package)
    {
        foreach (var leaf in package.IchBackboneLeaves)
        {
            if (leaf.Module is not ("m2" or "m3" or "m4" or "m5"))
            {
                throw new IchIndexXmlSectionMappingException(
                    package.ApplicationId,
                    package.SequenceNumber,
                    leaf.PlacementId,
                    leaf.CtdSection,
                    "leaf is not an ICH M2-M5 leaf");
            }

            if (!IchSectionDefinitions.Current.Definitions.Values.Any(definition => definition.SectionPath == leaf.CtdSection))
            {
                throw new IchIndexXmlSectionMappingException(
                    package.ApplicationId,
                    package.SequenceNumber,
                    leaf.PlacementId,
                    leaf.CtdSection,
                    "section is not in the supported ICH profile");
            }
        }
    }

    private static XElement BuildLeafElement(EctdLeaf leaf)
    {
        var attributes = new List<object>
        {
            new XAttribute("ID", leaf.LeafId),
            new XAttribute("operation", leaf.Operation),
            new XAttribute("checksum", leaf.Md5),
            new XAttribute("checksum-type", "md5"),
            new XAttribute(XlinkNamespace + "type", "simple"),
        };

        // delete leaf 不交付新文件：省略 xlink:href（DTD 中为 #IMPLIED），
        // 仅靠 modified-file 指向被删的历史 leaf。
        if (!IsDeleteOperation(leaf))
        {
            attributes.Add(new XAttribute(XlinkNamespace + "href", EctdLeafHref.FromBackbone(leaf, "index.xml")));
        }

        if (leaf.Lifecycle is not null)
        {
            attributes.Add(new XAttribute("modified-file", leaf.Lifecycle.BuildModifiedFileHref("index.xml")));
        }

        return new XElement("leaf",
            attributes,
            new XElement("title", leaf.Title));
    }

    private static bool IsDeleteOperation(EctdLeaf leaf)
        => string.Equals(leaf.Operation, "delete", StringComparison.OrdinalIgnoreCase);

}
