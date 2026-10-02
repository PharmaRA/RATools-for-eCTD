using System.Xml.Linq;

namespace RATools.Application.Ctd;

public static class CtdXmlAttributes
{
    public static string SchemaName(XName name) => name == XNamespace.Xml + "lang" ? "xml:lang" : name.ToString();
    public static XName XmlName(string name) => name == "xml:lang" ? XNamespace.Xml + "lang" : XName.Get(name);
}
