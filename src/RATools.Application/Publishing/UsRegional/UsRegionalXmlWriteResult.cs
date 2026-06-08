using System.Xml.Linq;

namespace RATools.Application.Publishing.UsRegional;

public sealed record UsRegionalXmlWriteResult(
    string FileName,
    string RelativePath,
    XDocument Document,
    string XmlContent);
