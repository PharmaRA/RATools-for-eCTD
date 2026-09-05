using RATools.Domain.Documents;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Publishing;

public sealed class LifecycleReferenceTests
{
    [Theory]
    [InlineData("us-fda-ectd-3.2.2", "m2.2", DocumentPlacementOperation.Replace)]
    [InlineData("us-fda-ectd-3.2.2", "m2.2", DocumentPlacementOperation.Append)]
    [InlineData("us-fda-ectd-3.2.2", "m2.2", DocumentPlacementOperation.Delete)]
    [InlineData("eu-ectd-3.2.2", "m2.2", DocumentPlacementOperation.Replace)]
    [InlineData("eu-ectd-3.2.2", "m2.2", DocumentPlacementOperation.Append)]
    [InlineData("eu-ectd-3.2.2", "m2.2", DocumentPlacementOperation.Delete)]
    [InlineData("us-fda-ectd-3.2.2", "m1.2", DocumentPlacementOperation.Replace)]
    [InlineData("us-fda-ectd-3.2.2", "m1.2", DocumentPlacementOperation.Append)]
    [InlineData("us-fda-ectd-3.2.2", "m1.2", DocumentPlacementOperation.Delete)]
    [InlineData("eu-ectd-3.2.2", "m1.2", DocumentPlacementOperation.Replace)]
    [InlineData("eu-ectd-3.2.2", "m1.2", DocumentPlacementOperation.Append)]
    [InlineData("eu-ectd-3.2.2", "m1.2", DocumentPlacementOperation.Delete)]
    public async Task PublishedLifecycleReference_ResolvesToHistoricalLeafAndDocument(
        string templateKey, string section, DocumentPlacementOperation operation)
    {
        using var fixture = new EctdWorkspaceFixture(templateKey);
        await fixture.AddSequenceAsync("0000");
        await fixture.AddSequenceAsync("0001");
        var original = await fixture.AddDocumentAsync("0000", section, "old.txt", "historical document");
        await fixture.AddDocumentAsync("0001", section, "current.txt", "current document", operation, original.Placement.Id);
        await fixture.WriteSequenceAsync("0000");
        await fixture.WriteSequenceAsync("0001");

        var xmlPath = section.StartsWith("m1.", StringComparison.Ordinal)
            ? fixture.Profile.BackboneXml!.Regional.RelativePath!
            : "index.xml";
        var currentXmlPath = Path.Combine(fixture.Application.WorkingDirectoryPath, "0001", xmlPath);
        var currentLeaf = Assert.Single(EctdWorkspaceFixture.ReadXml(currentXmlPath).Descendants("leaf"));
        var reference = Assert.IsType<string>(currentLeaf.Attribute("modified-file")?.Value);
        var currentXmlUri = new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = currentXmlPath }.Uri;
        var targetUri = new Uri(currentXmlUri, reference);

        Assert.Equal(Path.GetFullPath(Path.Combine(fixture.Application.WorkingDirectoryPath, "0000", xmlPath)), targetUri.LocalPath);
        Assert.False(string.IsNullOrEmpty(targetUri.Fragment));
        var historicalLeaf = Assert.Single(EctdWorkspaceFixture.ReadXml(targetUri.LocalPath).Descendants("leaf"),
            leaf => leaf.Attribute("ID")?.Value == Uri.UnescapeDataString(targetUri.Fragment[1..]));
        var documentHref = historicalLeaf.Attributes().Single(attribute => attribute.Name.LocalName == "href").Value;
        var documentUri = new Uri(new Uri(targetUri.GetLeftPart(UriPartial.Path)), documentHref);
        Assert.Equal(Path.GetFullPath(original.Document.StoragePath), documentUri.LocalPath);
        Assert.Equal("historical document", await File.ReadAllTextAsync(documentUri.LocalPath));
    }
}
