using System.Xml.Linq;
using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Applications;

public sealed class ApplicationImportRegionalTests
{
    [Theory]
    [InlineData("us-fda-ectd-3.2.2", "m1.1")]
    [InlineData("us-fda-ectd-3.2.2", "m1.2")]
    [InlineData("eu-ectd-3.2.2", "m1.0")]
    [InlineData("eu-ectd-3.2.2", "m1.2")]
    [InlineData("eu-ectd-3.2.2", "m1.3.1")]
    [InlineData("eu-ectd-3.2.2", "m1.responses")]
    [InlineData("eu-ectd-3.2.2", "m1.additional-data")]
    public async Task ImportAsync_RestoresPublishedRegionalAndIchDocuments(string templateKey, string section)
    {
        using var fixture = new EctdWorkspaceFixture(templateKey);
        await fixture.AddSequenceAsync("0000");
        var regional = await fixture.AddDocumentAsync("0000", section, "document.txt", "regional content");
        var ich = await fixture.AddDocumentAsync("0000", "m2.2", "document.txt", "ICH content");
        await fixture.WriteSequenceAsync("0000");

        var imported = await ImportAsync(fixture);

        Assert.Empty(imported.Result.Issues);
        Assert.Equal(2, imported.Result.ImportedDocumentCount);
        Assert.Equal(2, imported.Result.ImportedPlacementCount);
        foreach (var source in new[] { regional, ich })
        {
            var placement = Assert.Single(imported.Placements, item => item.CtdSection == source.Placement.CtdSection);
            var document = Assert.Single(imported.Documents, item => item.Id == placement.DocumentId);
            Assert.Equal(source.Document.StoragePath, document.StoragePath);
            Assert.Equal(source.Document.Sha256, document.Sha256);
            Assert.Equal(source.Document.Md5, document.Md5);
            Assert.Equal(source.Placement.Title, placement.Title);
        }

        var metadata = Assert.Single(imported.Application.Sequences).PublishingMetadata;
        Assert.NotNull(metadata);
        Assert.Equal("Test sponsor", metadata.ApplicantName);
        Assert.Equal("Test sequence", metadata.SequenceDescription);
        Assert.Equal(templateKey.StartsWith("eu-", StringComparison.Ordinal) ? "maa" : "original-application", metadata.SubmissionType);
        Assert.Equal("initial", metadata.SubmissionSubtype);
        if (imported.Application.Region == "US")
        {
            Assert.Equal("1571", metadata.FormType);
            Assert.Equal("test@example.test", metadata.Email);
        }
    }

    [Theory]
    [InlineData("us-fda-ectd-3.2.2")]
    [InlineData("eu-ectd-3.2.2")]
    public async Task ImportAsync_ReportsMissingRegionalBackbone(string templateKey)
    {
        using var fixture = new EctdWorkspaceFixture(templateKey);
        await fixture.AddSequenceAsync("0000");
        await fixture.AddDocumentAsync("0000", "m1.2", "regional.txt", "regional content");
        await fixture.AddDocumentAsync("0000", "m2.2", "ich.txt", "ICH content");
        await fixture.WriteSequenceAsync("0000");
        File.Delete(RegionalPath(fixture));

        var imported = await ImportAsync(fixture);

        Assert.Contains(imported.Result.Issues, issue => issue.Code == "SEQUENCE_REGIONAL_MISSING" && issue.Severity == "Warning");
        Assert.Equal("m2.2", Assert.Single(imported.Placements).CtdSection);
    }

    [Theory]
    [InlineData("missing", "SEQUENCE_FILE_MISSING")]
    [InlineData("checksum", "SEQUENCE_CHECKSUM_MISMATCH")]
    [InlineData("xml", "SEQUENCE_INDEX_INVALID")]
    [InlineData("outside", "SEQUENCE_FILE_OUTSIDE_WORKSPACE")]
    public async Task ImportAsync_ReportsRegionalFailures(string failure, string expectedCode)
    {
        using var fixture = new EctdWorkspaceFixture("us-fda-ectd-3.2.2");
        await fixture.AddSequenceAsync("0000");
        var source = await fixture.AddDocumentAsync("0000", "m1.2", "regional.txt", "regional content");
        await fixture.WriteSequenceAsync("0000");
        if (failure == "missing")
        {
            File.Delete(source.Document.StoragePath);
        }
        else if (failure == "checksum")
        {
            await File.WriteAllTextAsync(source.Document.StoragePath, "changed content");
        }
        else if (failure == "xml")
        {
            await File.WriteAllTextAsync(RegionalPath(fixture), "<broken");
        }
        else
        {
            var xml = EctdWorkspaceFixture.ReadXml(RegionalPath(fixture));
            xml.Descendants("leaf").Single().Attributes().Single(attribute => attribute.Name.LocalName == "href")
                .SetValue("../../../outside.txt");
            xml.Save(RegionalPath(fixture));
        }

        var imported = await ImportAsync(fixture);

        Assert.Contains(imported.Result.Issues, issue => issue.Code == expectedCode && issue.Severity == "Error");
        Assert.Equal(1, imported.Result.FailedSequenceCount);
        Assert.Empty(imported.Placements);
    }

    [Fact]
    public async Task ImportAsync_DoesNotImportRegionalBackboneReferenceAsADocument()
    {
        using var fixture = new EctdWorkspaceFixture("us-fda-ectd-3.2.2");
        await fixture.AddSequenceAsync("0000");
        await fixture.AddDocumentAsync("0000", "m1.2", "regional.txt", "regional content");
        await fixture.WriteSequenceAsync("0000");
        var indexPath = Path.Combine(fixture.Application.WorkingDirectoryPath, "0000", "index.xml");
        var xml = EctdWorkspaceFixture.ReadXml(indexPath);
        xml.Root!.Add(new XElement("m1-administrative-information-and-prescribing-information",
            new XElement("leaf", new XAttribute("ID", "regional-backbone"), new XAttribute("operation", "new"),
                new XAttribute(XName.Get("href", "http://www.w3.org/1999/xlink"), fixture.Profile.BackboneXml!.Regional.RelativePath!),
                new XElement("title", "Regional backbone"))));
        xml.Save(indexPath);

        var imported = await ImportAsync(fixture);

        Assert.Empty(imported.Result.Issues);
        Assert.Equal("m1.2", Assert.Single(imported.Placements).CtdSection);
        Assert.Equal("regional.txt", Assert.Single(imported.Documents).FileName);
    }

    private static string RegionalPath(EctdWorkspaceFixture fixture)
        => Path.Combine(fixture.Application.WorkingDirectoryPath, "0000", fixture.Profile.BackboneXml!.Regional.RelativePath!);

    private static async Task<ImportResult> ImportAsync(EctdWorkspaceFixture fixture)
    {
        var applications = new InMemoryApplicationRepository();
        var documents = new InMemoryDocumentRepository();
        var placements = new InMemoryDocumentPlacementRepository();
        var result = await new ApplicationImportService(applications, documents, placements, fixture.PathPolicy)
            .ImportAsync(new ImportApplicationRequest(fixture.Application.WorkingDirectoryPath, fixture.Application.EctdTemplateKey, "Import sponsor"));
        return new ImportResult(result, (await applications.GetAsync(result.ApplicationId))!, await documents.ListAsync(), await placements.ListAsync());
    }

    private sealed record ImportResult(ApplicationImportResultDto Result, SubmissionApplication Application,
        IReadOnlyCollection<SubmissionDocument> Documents, IReadOnlyCollection<DocumentPlacement> Placements);
}
