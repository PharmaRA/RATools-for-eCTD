using System.Xml.Linq;

namespace RATools.Application.Publishing.EuRegional;

public sealed record EuRegionalXmlWriteResult(
    string FileName,
    string RelativePath,
    XDocument Document,
    string XmlContent);
