using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using Xunit;

namespace RATools.Application.Tests.Publishing;

public sealed class BackboneServiceTests
{
    [Fact]
    public async Task GenerateAsync_UsesUniqueDocumentHrefWhenFileNamesCollide()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            "IND-0004",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow)]);

        var document1 = SubmissionDocument.Rehydrate(Guid.Parse("30000000-0000-0000-0000-000000000011"), "same.txt", "text/plain", 3, "hash1", "c:\\tmp\\one.txt", DateTime.UtcNow);
        var document2 = SubmissionDocument.Rehydrate(Guid.Parse("30000000-0000-0000-0000-000000000022"), "same.txt", "text/plain", 3, "hash2", "c:\\tmp\\two.txt", DateTime.UtcNow);

        var placement1 = new DocumentPlacement(document1.Id, application.Id, "0000", "m5.3.5.1", DocumentPlacementOperation.New, "Doc 1");
        var placement2 = new DocumentPlacement(document2.Id, application.Id, "0000", "m5.3.5.1", DocumentPlacementOperation.New, "Doc 2");

        var writer = new CapturingBackboneFileWriter();
        var service = new BackboneService(
            new BackboneStubApplicationRepository(application),
            new BackboneStubPlacementRepository([placement1, placement2]),
            new BackboneStubDocumentRepository([document1, document2]),
            writer);

        await service.GenerateAsync(new GenerateBackboneRequest(application.Id, "0000", "publish-report.json", "0000.zip"));

        Assert.Contains("documents/30000000000000000000000000000011_same.txt", writer.XmlContent);
        Assert.Contains("documents/30000000000000000000000000000022_same.txt", writer.XmlContent);
    }

    [Fact]
    public async Task GenerateAsync_AddsMoreRealisticLeafAndBackboneMetadata()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("31000000-0000-0000-0000-000000000001"),
            "IND-0004",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow)]);

        var document = SubmissionDocument.Rehydrate(Guid.Parse("31000000-0000-0000-0000-000000000011"), "report.pdf", "application/pdf", 3, "hash1", "c:\\tmp\\one.pdf", DateTime.UtcNow);
        var placement = new DocumentPlacement(document.Id, application.Id, "0000", "m5.3.5.1", DocumentPlacementOperation.New, "Report");

        var writer = new CapturingBackboneFileWriter();
        var service = new BackboneService(
            new BackboneStubApplicationRepository(application),
            new BackboneStubPlacementRepository([placement]),
            new BackboneStubDocumentRepository([document]),
            writer);

        await service.GenerateAsync(new GenerateBackboneRequest(application.Id, "0000", "publish-report.json", "0000.zip"));

        Assert.Contains("dtd-version=\"3.2.2\"", writer.XmlContent);
        Assert.Contains("xlink:type=\"simple\"", writer.XmlContent);
        Assert.Contains("checksum-type=\"md5\"", writer.XmlContent);
    }

    [Fact]
    public async Task GenerateAsync_UsesCumulativeSectionIdsForNestedSections()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("32000000-0000-0000-0000-000000000001"),
            "IND-0004",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow)]);

        var document = SubmissionDocument.Rehydrate(Guid.Parse("32000000-0000-0000-0000-000000000011"), "report.pdf", "application/pdf", 3, "hash1", "c:\\tmp\\one.pdf", DateTime.UtcNow);
        var placement = new DocumentPlacement(document.Id, application.Id, "0000", "m5.3.5.1", DocumentPlacementOperation.New, "Report");

        var writer = new CapturingBackboneFileWriter();
        var service = new BackboneService(
            new BackboneStubApplicationRepository(application),
            new BackboneStubPlacementRepository([placement]),
            new BackboneStubDocumentRepository([document]),
            writer);

        await service.GenerateAsync(new GenerateBackboneRequest(application.Id, "0000", "publish-report.json", "0000.zip"));

        Assert.Contains("<ectd:section id=\"m5\"", writer.XmlContent);
        Assert.Contains("<ectd:section id=\"m5.3\"", writer.XmlContent);
        Assert.Contains("<ectd:section id=\"m5.3.5\"", writer.XmlContent);
        Assert.Contains("<ectd:section id=\"m5.3.5.1\"", writer.XmlContent);
    }
}

file sealed class CapturingBackboneFileWriter : IBackboneFileWriter
{
    public string XmlContent { get; private set; } = string.Empty;

    public Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(Guid applicationId, string sequenceNumber, string fileName, string content, string reportFileName, string packageFileName, string reportContent, IReadOnlyCollection<SubmissionDocument> documents, CancellationToken cancellationToken = default)
    {
        XmlContent = content;
        return Task.FromResult(("index.xml", reportFileName, packageFileName));
    }
}

file sealed class BackboneStubApplicationRepository(SubmissionApplication application) : IApplicationRepository
{
    public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(id == application.Id ? application : null);
    public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyCollection<SubmissionApplication>)[application]);
}

file sealed class BackboneStubPlacementRepository(IReadOnlyCollection<DocumentPlacement> placements) : IDocumentPlacementRepository
{
    public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(placements);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => Task.FromResult(placements.Where(x => x.ApplicationId == applicationId).ToArray() as IReadOnlyCollection<DocumentPlacement>);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => Task.FromResult(placements.Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber).ToArray() as IReadOnlyCollection<DocumentPlacement>);
}

file sealed class BackboneStubDocumentRepository(IReadOnlyCollection<SubmissionDocument> documents) : IDocumentRepository
{
    public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(documents.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(documents);
}
