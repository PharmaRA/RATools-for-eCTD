using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.Ich;

public sealed class IchIndexXmlWriter : IIchIndexXmlWriter
{
    private static readonly XNamespace EctdNamespace = "http://www.ich.org/ectd";
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";

    public IchIndexXmlWriteResult Write(EctdSequencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var root = new XElement(EctdNamespace + "ectd",
            new XAttribute(XNamespace.Xmlns + "ectd", EctdNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", "3.2"));
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null),
            root);

        return new IchIndexXmlWriteResult("index.xml", document, document.ToString(SaveOptions.DisableFormatting));
    }
}
