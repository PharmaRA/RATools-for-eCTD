using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Validation.Profiles;

namespace RATools.Application.Publishing.Ich;

public sealed class IchIndexXmlWriter : IIchIndexXmlWriter
{
    private static readonly XNamespace EctdNamespace = "http://www.ich.org/ectd";
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";
    private static readonly SectionPathNode[] IchTopLevelNodes = FdaEctd322.Root.Children
        .Where(x => x.SectionPath is "m2" or "m3" or "m4" or "m5")
        .Select(BuildSectionPathNode)
        .ToArray();

    public IchIndexXmlWriteResult Write(EctdSequencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var root = new XElement(EctdNamespace + "ectd",
            new XAttribute(XNamespace.Xmlns + "ectd", EctdNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", "3.2"));
        var leavesBySection = package.IchBackboneLeaves
            .GroupBy(x => x.CtdSection, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(leaf => leaf.LeafId, StringComparer.OrdinalIgnoreCase).ToArray(),
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
            new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null),
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
        return new XElement("leaf",
            new XAttribute("ID", leaf.LeafId),
            new XAttribute("operation", leaf.Operation),
            new XAttribute("checksum", leaf.Sha256),
            new XAttribute("checksum-type", "sha256"),
            new XAttribute(XlinkNamespace + "type", "simple"),
            new XAttribute(XlinkNamespace + "href", leaf.Href),
            new XElement("title", leaf.Title));
    }

    private sealed record SectionPathNode(
        string ElementName,
        string SectionPath,
        IReadOnlyCollection<SectionPathNode> Children);
}
