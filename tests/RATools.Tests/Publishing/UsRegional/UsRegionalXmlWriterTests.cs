using System.Xml.Linq;
using RATools.Application.Publishing.UsRegional;

namespace RATools.Tests.Publishing.UsRegional;

public sealed class UsRegionalXmlWriterTests
{
    [Fact]
    public void WriteResult_ExposesExpectedContract()
    {
        var document = new XDocument(new XElement("root"));

        var result = new UsRegionalXmlWriteResult(
            "us-regional.xml",
            "m1/us/us-regional.xml",
            document,
            "<root />");

        Assert.Equal("us-regional.xml", result.FileName);
        Assert.Equal("m1/us/us-regional.xml", result.RelativePath);
        Assert.Same(document, result.Document);
        Assert.Equal("<root />", result.XmlContent);
    }
}
