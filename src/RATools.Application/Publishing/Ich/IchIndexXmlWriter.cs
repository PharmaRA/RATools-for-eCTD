using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Validation.Profiles;

namespace RATools.Application.Publishing.Ich;

public sealed class IchIndexXmlWriter : IIchIndexXmlWriter
{
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";
    private static readonly SectionPathNode[] IchTopLevelNodes = FdaEctd322.Root.Children
        .Where(x => x.SectionPath is "m2" or "m3" or "m4" or "m5")
        .Select(BuildSectionPathNode)
        .ToArray();
    private static readonly Dictionary<string, SectionPathNode> SectionByPath = IchTopLevelNodes
        .SelectMany(Flatten)
        .ToDictionary(x => x.SectionPath, x => x, StringComparer.OrdinalIgnoreCase);

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

        var leavesBySection = package.IchBackboneLeaves
            .Select((leaf, index) => new IndexedLeaf(leaf, index))
            .GroupBy(x => x.Leaf.CtdSection, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(leaf => leaf.Index).ThenBy(leaf => leaf.Leaf.LeafId, StringComparer.OrdinalIgnoreCase).Select(leaf => leaf.Leaf).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var module in IchTopLevelNodes)
        {
            var element = BuildSectionElement(module, leavesBySection);
            if (element is not null)
            {
                root.Add(element);
            }
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XDocumentType(xmlProfile.DocumentTypeName, null, xmlProfile.DtdSystemId, null),
            root);

        return new IchIndexXmlWriteResult("index.xml", document, document.ToString(SaveOptions.DisableFormatting));
    }

    private static SectionPathNode BuildSectionPathNode(SectionDictionaryManualNode node)
    {
        return new SectionPathNode(
            node.ElementName,
            node.SectionPath,
            node.Children.Select(BuildSectionPathNode).ToArray());
    }

    private static IEnumerable<SectionPathNode> Flatten(SectionPathNode node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
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

            if (!SectionByPath.ContainsKey(leaf.CtdSection))
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

    private static XElement? BuildSectionElement(
        SectionPathNode node,
        IReadOnlyDictionary<string, EctdLeaf[]> leavesBySection)
    {
        leavesBySection.TryGetValue(node.SectionPath, out var leaves);
        var childElements = node.Children
            .Select(child => BuildSectionElement(child, leavesBySection))
            .Where(child => child is not null)
            .Cast<XElement>()
            .ToArray();

        if ((leaves is null || leaves.Length == 0) && childElements.Length == 0)
        {
            return null;
        }

        var element = new XElement(node.ElementName);
        if (leaves is not null)
        {
            foreach (var leaf in leaves)
            {
                element.Add(BuildLeafElement(leaf));
            }
        }

        element.Add(childElements);
        return element;
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
            attributes.Add(new XAttribute(XlinkNamespace + "href", leaf.Href));
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

    private sealed record IndexedLeaf(EctdLeaf Leaf, int Index);

    private sealed record SectionPathNode(
        string ElementName,
        string SectionPath,
        IReadOnlyCollection<SectionPathNode> Children);
}
