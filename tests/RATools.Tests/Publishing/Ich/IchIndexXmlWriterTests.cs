using System.Xml.Linq;
using RATools.Application.Publishing.Ich;

namespace RATools.Tests.Publishing.Ich;

public sealed class IchIndexXmlWriterTests
{
    [Fact]
    public void WriteResult_ExposesExpectedContract()
    {
        var document = new XDocument(new XElement("root"));

        var result = new IchIndexXmlWriteResult("index.xml", document, "<root />");

        Assert.Equal("index.xml", result.FileName);
        Assert.Same(document, result.Document);
        Assert.Equal("<root />", result.XmlContent);
    }
}
