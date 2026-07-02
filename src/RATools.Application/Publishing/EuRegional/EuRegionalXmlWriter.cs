using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;

namespace RATools.Application.Publishing.EuRegional;

public sealed class EuRegionalXmlWriter : IEuRegionalXmlWriter
{
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";

    public EuRegionalXmlWriteResult Write(EctdSequencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var xmlProfile = package.BackboneXml.Regional;
        if (!ReferenceEquals(package.BackboneXml, BackboneXmlProfiles.EuEctd322Regional)
            && !string.Equals(xmlProfile.RelativePath, BackboneXmlProfiles.EuEctd322Regional.Regional.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new EuRegionalXmlWriterException("Unable to generate EU regional XML: package does not use an EU regional backbone profile.");
        }

        var relativePath = RequireRelativePath(xmlProfile.RelativePath);
        XNamespace regionalNamespace = xmlProfile.Namespace;
        var root = new XElement(regionalNamespace + xmlProfile.RootElementName,
            new XAttribute(XNamespace.Xmlns + "eu-regional", regionalNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", xmlProfile.DtdVersion));

        if (package.Module1Leaves.Count > 0)
        {
            root.Add(new XElement("m1-eu-regional",
                package.Module1Leaves
                    .OrderBy(leaf => leaf.CtdSection, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(leaf => leaf.LeafId, StringComparer.OrdinalIgnoreCase)
                    .Select(leaf => BuildLeafElement(leaf, relativePath))));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XDocumentType(xmlProfile.DocumentTypeName, null, xmlProfile.DtdSystemId, null),
            root);

        return new EuRegionalXmlWriteResult(
            "eu-regional.xml",
            relativePath,
            document,
            document.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement BuildLeafElement(EctdLeaf leaf, string regionalRelativePath)
    {
        if (!string.Equals(leaf.Module, "m1", StringComparison.OrdinalIgnoreCase))
        {
            throw new EuRegionalXmlWriterException($"Unable to generate EU regional XML: leaf '{leaf.LeafId}' is not a Module 1 leaf.");
        }

        return new XElement("leaf",
            new XAttribute("ID", leaf.LeafId),
            new XAttribute("operation", leaf.Operation),
            new XAttribute("checksum", leaf.Md5),
            new XAttribute("checksum-type", "md5"),
            new XAttribute(XlinkNamespace + "type", "simple"),
            new XAttribute(XlinkNamespace + "href", BuildRegionalHref(leaf.Href, regionalRelativePath)),
            new XElement("title", leaf.Title));
    }

    private static string BuildRegionalHref(string sequenceRootHref, string regionalRelativePath)
    {
        var module1Prefix = GetDirectoryName(regionalRelativePath);
        return sequenceRootHref.StartsWith(module1Prefix, StringComparison.OrdinalIgnoreCase)
            ? sequenceRootHref[module1Prefix.Length..]
            : sequenceRootHref;
    }

    private static string GetDirectoryName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex < 0 ? string.Empty : normalized[..(slashIndex + 1)];
    }

    private static string RequireRelativePath(string? relativePath)
        => string.IsNullOrWhiteSpace(relativePath)
            ? throw new EuRegionalXmlWriterException("Unable to generate EU regional XML: regional relative path is required.")
            : relativePath;
}
