using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;
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
        var isEu = templateKey.StartsWith("eu-", StringComparison.Ordinal);
        string? coverPath = null;
        string? deletedCoverPath = null;
        if (isEu)
        {
            // Typed EU output requires a real cover context. A second historical
            // delete leaf supplies it without introducing a new payload.
            var cover = await fixture.AddDocumentAsync("0000", "m1.0", "cover.txt", "historical cover");
            var deletedCover = await fixture.AddDocumentAsync("0001", "m1.0", "cover-placeholder.txt", "unused cover", DocumentPlacementOperation.Delete, cover.Placement.Id);
            coverPath = cover.Document.StoragePath;
            deletedCoverPath = deletedCover.Document.StoragePath;
        }
        await fixture.WriteSequenceAsync("0000");
        await fixture.WriteSequenceAsync("0001");
        fixture.RetainMissingFile(deleted.Document.StoragePath);
        if (deletedCoverPath is not null) fixture.RetainMissingFile(deletedCoverPath);
        var xmlPath = section.StartsWith("m1.", StringComparison.Ordinal) ? fixture.Profile.BackboneXml!.Regional.RelativePath! : "index.xml";
        var originalPath = Path.Combine(fixture.Application.WorkingDirectoryPath, "0000", xmlPath);
        var originalXml = EctdWorkspaceFixture.ReadXml(originalPath);
        var originalLeaf = Assert.Single(originalXml.Descendants("leaf"), leaf => leaf.Element("title")?.Value == "original.txt");
        var originalLeafId = originalLeaf.Attribute("ID")!.Value;
        if (externalLeafId)
        {
            originalLeaf.SetAttributeValue("ID", "external-original-123");
            originalXml.Save(originalPath);
            var currentPath = Path.Combine(fixture.Application.WorkingDirectoryPath, "0001", xmlPath);
            var currentXml = EctdWorkspaceFixture.ReadXml(currentPath);
            var attribute = Assert.Single(currentXml.Descendants("leaf"), leaf => leaf.Element("title")?.Value == "placeholder.txt").Attribute("modified-file")!;
            attribute.Value = attribute.Value.Replace(originalLeafId, "external-original-123", StringComparison.Ordinal);
            currentXml.Save(currentPath);
            originalLeafId = "external-original-123";
        }

        await using var db = new RAToolsDbContext(new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        // Import uses one coherent InMemory application/revision pair. EF's
        // application repository delegates existing revision writes to its own
        // SQL revision store and cannot back InMemoryWorkspaceRevisionStore.
        var importApplications = new InMemoryApplicationRepository();
        var documents = new EfCoreDocumentRepository(db);
        var placements = new EfCoreDocumentPlacementRepository(db);
        var revisions = new InMemoryWorkspaceRevisionStore(importApplications);
        var nodes = new InMemoryCtdNodeRepository(revisions, placements);
        var regionalMetadata = new InMemoryRegionalMetadataStore();
        var imports = new InMemoryApplicationImportStore(importApplications, documents, placements, nodes, regionalMetadata);
        var snapshots = ImportAcceptanceFixture.MemorySnapshots();
        var imported = await new ApplicationImportService(importApplications, imports, fixture.PathPolicy, snapshots)
            .ImportAsync(new ImportApplicationRequest(fixture.Application.WorkingDirectoryPath, templateKey, "Sponsor"));

        AssertExpectedImportWarnings(imported, 2);
        Assert.Equal(isEu ? 2 : 1, imported.ImportedDocumentCount);
        Assert.Equal(isEu ? 4 : 2, imported.ImportedPlacementCount);
        // Retain the EF InMemory mapping round trip for the imported application,
        // documents and placements; this does not exercise relational transactions.
        var importedApplication = (await importApplications.GetAsync(imported.ApplicationId))!;
        var applications = new EfCoreApplicationRepository(db);
        await applications.AddAsync(importedApplication);
        db.ChangeTracker.Clear();
        var reloadedApplication = (await applications.GetAsync(imported.ApplicationId))!;
        foreach (var sequence in importedApplication.Sequences)
            Assert.Equal(sequence.WorkspaceRevision, reloadedApplication.Sequences.Single(item => item.SequenceNumber == sequence.SequenceNumber).WorkspaceRevision);
        var historicalPlacements = await placements.ListBySequenceAsync(imported.ApplicationId, "0000");
        var deletePlacements = await placements.ListBySequenceAsync(imported.ApplicationId, "0001");
        var historicalPlacement = Assert.Single(historicalPlacements, item => item.CtdSection == section);
        var deletePlacement = Assert.Single(deletePlacements, item => item.CtdSection == section);
        Assert.Equal(DocumentPlacementOperation.Delete, deletePlacement.Operation);
        Assert.Equal(historicalPlacement.Id, deletePlacement.LifecycleTargetPlacementId);
        Assert.Equal(historicalPlacement.DocumentId, deletePlacement.DocumentId);
        Assert.All(deletePlacements, item =>
        {
            Assert.Equal(DocumentPlacementOperation.Delete, item.Operation);
            var target = Assert.Single(historicalPlacements, prior => prior.Id == item.LifecycleTargetPlacementId);
            Assert.Equal(target.DocumentId, item.DocumentId);
        });

        var boundary = new DocumentStorageBoundary(fixture.PathPolicy);
        var validation = await new SequenceValidationService(applications, placements, documents,
            new AuditLogService(new InMemoryAuditLogRepository()),
            new ConfigurationValidationProfileProvider(Options.Create(new ValidationProfileOptions())),
            NullLogger<SequenceValidationService>.Instance, boundary)
            .ValidateAsync(new ValidateSequenceRequest(imported.ApplicationId, "0001"));
        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Select(issue => issue.Message)));
        IStandardsProfileProvider standards = templateKey.StartsWith("eu-", StringComparison.Ordinal)
            ? new EuEctd322StandardsProfileProvider() : new FdaEctd322StandardsProfileProvider();
        var package = await new EctdPackageModelBuilder(applications, placements, documents, standards, boundary, nodes, revisions,
                imports, regionalMetadataStore: regionalMetadata, standardsSnapshots: snapshots)
            .BuildAsync(new BuildEctdPackageRequest(imported.ApplicationId, "0001"));
        var importedRegional = (await regionalMetadata.GetHeadAsync(imported.ApplicationId, "0001"))!;
        Assert.NotNull(package.RegionalMetadata);
        Assert.Equal(importedRegional.Id, package.RegionalMetadata.Id);
        Assert.Equal(importedRegional.Digest, package.RegionalMetadata.Digest);
        Assert.Empty(package.PublishedFiles);
        var output = section.StartsWith("m2.", StringComparison.Ordinal) ? new IchIndexXmlWriter().Write(package).Document
            : templateKey.StartsWith("eu-", StringComparison.Ordinal) ? new EuRegionalXmlWriter().Write(package).Document
            : new UsRegionalXmlWriter().Write(package).Document;
        var leaves = output.Descendants("leaf").ToArray();
        Assert.Equal(isEu && section == "m1.2" ? 2 : 1, leaves.Length);
        Assert.All(leaves, item =>
        {
            Assert.DoesNotContain(item.Attributes(), attribute => attribute.Name.LocalName == "href");
            Assert.Equal(string.Empty, item.Attribute("checksum")?.Value);
        });
        var leaf = Assert.Single(leaves, item => item.Attribute("ID")?.Value == deletePlacement.LeafId);
        Assert.EndsWith($"#{originalLeafId}", leaf.Attribute("modified-file")?.Value, StringComparison.Ordinal);
        Assert.Equal("historical content", await File.ReadAllTextAsync(original.Document.StoragePath));
        if (coverPath is not null) Assert.Equal("historical cover", await File.ReadAllTextAsync(coverPath));
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

        var result = await ImportTestServices.Create(applications, documents, placements, fixture.PathPolicy)
            .ImportAsync(new ImportApplicationRequest(fixture.Application.WorkingDirectoryPath, templateKey, "Sponsor"));

        AssertExpectedImportWarnings(result, 3);
        var original = Assert.Single(await placements.ListBySequenceAsync(result.ApplicationId, "0000"));
        var current = Assert.Single(await placements.ListBySequenceAsync(result.ApplicationId, "0002"));
        Assert.Equal(original.Id, current.LifecycleTargetPlacementId);
    }

    private static void AssertExpectedImportWarnings(ApplicationImportResultDto result, int sequenceCount)
    {
        Assert.Equal(sequenceCount, result.Issues.Count(issue => issue.Code == "RegionalQualificationPending"));
        Assert.All(result.Issues, issue =>
        {
            Assert.Equal("Warning", issue.Severity);
            Assert.True(issue.Code is "NODE_SCHEMA_NOT_AVAILABLE" or "RegionalUnsupportedNamespace" or
                "RegionalUnmappedValue" or "RegionalUnknownAttribute" or "RegionalQualificationPending" or
                "RegionalContextUnbound", $"Unexpected import diagnostic: {issue.Code}");
        });
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
        fixture.RetainMissingFile(deleted.Document.StoragePath);
        var currentPath = Path.Combine(fixture.Application.WorkingDirectoryPath, "0001", "index.xml");
        var xml = EctdWorkspaceFixture.ReadXml(currentPath);
        xml.Descendants("leaf").Single(leaf => leaf.Attribute("operation")?.Value == "delete").SetAttributeValue("modified-file", reference);
        xml.Save(currentPath);
        var applications = new InMemoryApplicationRepository();
        var documents = new InMemoryDocumentRepository();
        var placements = new InMemoryDocumentPlacementRepository();

        var result = await ImportTestServices.Create(applications, documents, placements, fixture.PathPolicy)
            .ImportAsync(new ImportApplicationRequest(fixture.Application.WorkingDirectoryPath, fixture.Application.EctdTemplateKey, "Sponsor"));

        Assert.Contains(result.Issues, issue => issue.Code == expectedCode && issue.Severity == "Error");
        Assert.Equal(1, result.FailedSequenceCount);
        Assert.Equal(1, result.ImportedSequenceCount);
        Assert.Equal("0000", Assert.Single(await placements.ListAsync()).SequenceNumber);
        Assert.Single(await documents.ListAsync());
    }
}
