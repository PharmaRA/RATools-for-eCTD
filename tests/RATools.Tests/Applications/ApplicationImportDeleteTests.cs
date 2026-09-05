using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Application.Auditing;
using RATools.Application.Documents;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Validation;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Applications;

public sealed class ApplicationImportDeleteTests
{
    [Theory]
    [InlineData("us-fda-ectd-3.2.2", "m2.2", false)]
    [InlineData("us-fda-ectd-3.2.2", "m2.2", true)]
    [InlineData("eu-ectd-3.2.2", "m2.2", false)]
    [InlineData("eu-ectd-3.2.2", "m2.2", true)]
    [InlineData("us-fda-ectd-3.2.2", "m1.2", false)]
    [InlineData("us-fda-ectd-3.2.2", "m1.2", true)]
    [InlineData("eu-ectd-3.2.2", "m1.2", false)]
    [InlineData("eu-ectd-3.2.2", "m1.2", true)]
    public async Task ImportAsync_RepublishesDeleteWithoutANewFileAndPreservesHistoricalLeafId(string templateKey, string section, bool externalLeafId)
    {
        using var fixture = new EctdWorkspaceFixture(templateKey);
        await fixture.AddSequenceAsync("0000");
        await fixture.AddSequenceAsync("0001");
        var original = await fixture.AddDocumentAsync("0000", section, "original.txt", "historical content");
        var deleted = await fixture.AddDocumentAsync("0001", section, "placeholder.txt", "unused content", DocumentPlacementOperation.Delete, original.Placement.Id);
        await fixture.WriteSequenceAsync("0000");
        await fixture.WriteSequenceAsync("0001");
        File.Delete(deleted.Document.StoragePath);
        var xmlPath = section.StartsWith("m1.", StringComparison.Ordinal) ? fixture.Profile.BackboneXml!.Regional.RelativePath! : "index.xml";
        var originalPath = Path.Combine(fixture.Application.WorkingDirectoryPath, "0000", xmlPath);
        var originalXml = EctdWorkspaceFixture.ReadXml(originalPath);
        var originalLeafId = originalXml.Descendants("leaf").Single().Attribute("ID")!.Value;
        if (externalLeafId)
        {
            originalXml.Descendants("leaf").Single().SetAttributeValue("ID", "external-original-123");
            originalXml.Save(originalPath);
            var currentPath = Path.Combine(fixture.Application.WorkingDirectoryPath, "0001", xmlPath);
            var currentXml = EctdWorkspaceFixture.ReadXml(currentPath);
            var attribute = currentXml.Descendants("leaf").Single().Attribute("modified-file")!;
            attribute.Value = attribute.Value.Replace(originalLeafId, "external-original-123", StringComparison.Ordinal);
            currentXml.Save(currentPath);
            originalLeafId = "external-original-123";
        }

        await using var db = new RAToolsDbContext(new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var applications = new EfCoreApplicationRepository(db);
        var documents = new EfCoreDocumentRepository(db);
        var placements = new EfCoreDocumentPlacementRepository(db);
        var imported = await new ApplicationImportService(applications, documents, placements, fixture.PathPolicy)
            .ImportAsync(new ImportApplicationRequest(fixture.Application.WorkingDirectoryPath, templateKey, "Sponsor"));

        Assert.Empty(imported.Issues);
        Assert.Equal(1, imported.ImportedDocumentCount);
        Assert.Equal(2, imported.ImportedPlacementCount);
        db.ChangeTracker.Clear();
        var historicalPlacement = Assert.Single(await placements.ListBySequenceAsync(imported.ApplicationId, "0000"));
        var deletePlacement = Assert.Single(await placements.ListBySequenceAsync(imported.ApplicationId, "0001"));
        Assert.Equal(DocumentPlacementOperation.Delete, deletePlacement.Operation);
        Assert.Equal(historicalPlacement.Id, deletePlacement.LifecycleTargetPlacementId);
        Assert.Equal(historicalPlacement.DocumentId, deletePlacement.DocumentId);

        var boundary = new DocumentStorageBoundary(fixture.PathPolicy);
        var validation = await new SequenceValidationService(applications, placements, documents,
            new AuditLogService(new InMemoryAuditLogRepository()),
            new ConfigurationValidationProfileProvider(Options.Create(new ValidationProfileOptions())),
            NullLogger<SequenceValidationService>.Instance, boundary)
            .ValidateAsync(new ValidateSequenceRequest(imported.ApplicationId, "0001"));
        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Select(issue => issue.Message)));
        IStandardsProfileProvider standards = templateKey.StartsWith("eu-", StringComparison.Ordinal)
            ? new EuEctd322StandardsProfileProvider() : new FdaEctd322StandardsProfileProvider();
        var package = await new EctdPackageModelBuilder(applications, placements, documents, standards, boundary)
            .BuildAsync(new BuildEctdPackageRequest(imported.ApplicationId, "0001"));
        Assert.Empty(package.PublishedFiles);
        var output = section.StartsWith("m2.", StringComparison.Ordinal) ? new IchIndexXmlWriter().Write(package).Document
            : templateKey.StartsWith("eu-", StringComparison.Ordinal) ? new EuRegionalXmlWriter().Write(package).Document
            : new UsRegionalXmlWriter().Write(package).Document;
        var leaf = Assert.Single(output.Descendants("leaf"));
        Assert.DoesNotContain(leaf.Attributes(), attribute => attribute.Name.LocalName == "href");
        Assert.Equal(string.Empty, leaf.Attribute("checksum")?.Value);
        Assert.EndsWith($"#{originalLeafId}", leaf.Attribute("modified-file")?.Value, StringComparison.Ordinal);
        Assert.Equal("historical content", await File.ReadAllTextAsync(original.Document.StoragePath));
    }

    [Theory]
    [InlineData("us-fda-ectd-3.2.2", "m2.2", DocumentPlacementOperation.Replace)]
    [InlineData("us-fda-ectd-3.2.2", "m2.2", DocumentPlacementOperation.Append)]
    [InlineData("eu-ectd-3.2.2", "m1.2", DocumentPlacementOperation.Replace)]
    [InlineData("eu-ectd-3.2.2", "m1.2", DocumentPlacementOperation.Append)]
    public async Task ImportAsync_ResolvesExactXmlLeafWhenHistoricalFileNamesRepeat(string templateKey, string section, DocumentPlacementOperation operation)
    {
        using var fixture = new EctdWorkspaceFixture(templateKey);
        foreach (var number in new[] { "0000", "0001", "0002" })
        {
            await fixture.AddSequenceAsync(number);
        }
        var target = await fixture.AddDocumentAsync("0000", section, "same.txt", "target content");
        await fixture.AddDocumentAsync("0001", section, "same.txt", "another historical document");
        await fixture.AddDocumentAsync("0002", section, "current.txt", "current content", operation, target.Placement.Id);
        foreach (var number in new[] { "0000", "0001", "0002" })
        {
            await fixture.WriteSequenceAsync(number);
        }
        var applications = new InMemoryApplicationRepository();
        var documents = new InMemoryDocumentRepository();
        var placements = new InMemoryDocumentPlacementRepository();

        var result = await new ApplicationImportService(applications, documents, placements, fixture.PathPolicy)
            .ImportAsync(new ImportApplicationRequest(fixture.Application.WorkingDirectoryPath, templateKey, "Sponsor"));

        Assert.Empty(result.Issues);
        var original = Assert.Single(await placements.ListBySequenceAsync(result.ApplicationId, "0000"));
        var current = Assert.Single(await placements.ListBySequenceAsync(result.ApplicationId, "0002"));
        Assert.Equal(original.Id, current.LifecycleTargetPlacementId);
    }

    [Theory]
    [InlineData(null, "LIFECYCLE_TARGET_MISSING")]
    [InlineData("../0000/index.xml#missing", "LIFECYCLE_TARGET_NOT_IMPORTED")]
    [InlineData("../../outside/index.xml#missing", "LIFECYCLE_TARGET_NOT_IMPORTED")]
    [InlineData("index.xml#missing", "LIFECYCLE_TARGET_NOT_IMPORTED")]
    public async Task ImportAsync_DiscardsFailedSequenceWhenDeleteTargetCannotBeResolved(string? reference, string expectedCode)
    {
        using var fixture = new EctdWorkspaceFixture("us-fda-ectd-3.2.2");
        await fixture.AddSequenceAsync("0000");
        await fixture.AddSequenceAsync("0001");
        var original = await fixture.AddDocumentAsync("0000", "m2.2", "original.txt", "historical content");
        await fixture.AddDocumentAsync("0001", "m2.2", "contents.txt", "valid leaf before invalid deletion");
        var deleted = await fixture.AddDocumentAsync("0001", "m2.2", "placeholder.txt", "unused content", DocumentPlacementOperation.Delete, original.Placement.Id);
        await fixture.WriteSequenceAsync("0000");
        await fixture.WriteSequenceAsync("0001");
        File.Delete(deleted.Document.StoragePath);
        var currentPath = Path.Combine(fixture.Application.WorkingDirectoryPath, "0001", "index.xml");
        var xml = EctdWorkspaceFixture.ReadXml(currentPath);
        xml.Descendants("leaf").Single(leaf => leaf.Attribute("operation")?.Value == "delete").SetAttributeValue("modified-file", reference);
        xml.Save(currentPath);
        var applications = new InMemoryApplicationRepository();
        var documents = new InMemoryDocumentRepository();
        var placements = new InMemoryDocumentPlacementRepository();

        var result = await new ApplicationImportService(applications, documents, placements, fixture.PathPolicy)
            .ImportAsync(new ImportApplicationRequest(fixture.Application.WorkingDirectoryPath, fixture.Application.EctdTemplateKey, "Sponsor"));

        Assert.Contains(result.Issues, issue => issue.Code == expectedCode && issue.Severity == "Error");
        Assert.Equal(1, result.FailedSequenceCount);
        Assert.Equal(1, result.ImportedSequenceCount);
        Assert.Equal("0000", Assert.Single(await placements.ListAsync()).SequenceNumber);
        Assert.Single(await documents.ListAsync());
    }
}
