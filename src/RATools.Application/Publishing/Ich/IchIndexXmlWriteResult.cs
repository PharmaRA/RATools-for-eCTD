using System.Xml.Linq;

namespace RATools.Application.Publishing.Ich;

public sealed record IchIndexXmlWriteResult(
    string FileName,
    XDocument Document,
    string XmlContent);
