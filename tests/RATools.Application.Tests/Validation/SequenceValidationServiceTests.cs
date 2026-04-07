using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using Xunit;

namespace RATools.Application.Tests.Validation;

public sealed class SequenceValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsErrorForInvalidSectionPath()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("61000000-0000-0000-0000-000000000001"),
            "IND-0007",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow)]);

        var document = SubmissionDocument.Rehydrate(
            Guid.Parse("61000000-0000-0000-0000-000000000011"),
            "report.pdf",
            "application/pdf",
            3,
            "hash1",
            ValidationTestFiles.CreateTempFile("report.pdf"),
            DateTime.UtcNow);

        var placement = new DocumentPlacement(document.Id, application.Id, "0000", "m5..1", DocumentPlacementOperation.New, "Report");
        var service = new SequenceValidationService(
            new ValidationStubApplicationRepository(application),
            new ValidationStubPlacementRepository([placement]),
            new ValidationStubDocumentRepository([document]),
            new ValidationStubAuditLogService(),
            new ValidationStubProfileProvider());

        var report = await service.ValidateAsync(new ValidateSequenceRequest(application.Id, "0000"));

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, x => x.Code == "INVALID_SECTION_PATH" && x.Severity == "Error");
    }
}

file static class ValidationTestFiles
{
    public static string CreateTempFile(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileName}");
        File.WriteAllText(path, "test");
        return path;
    }
}

file sealed class ValidationStubApplicationRepository(SubmissionApplication application) : IApplicationRepository
{
    public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(id == application.Id ? application : null);
    public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyCollection<SubmissionApplication>)[application]);
}

file sealed class ValidationStubPlacementRepository(IReadOnlyCollection<DocumentPlacement> placements) : IDocumentPlacementRepository
{
    public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(placements);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyCollection<DocumentPlacement>)placements.Where(x => x.ApplicationId == applicationId).ToArray());
    public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyCollection<DocumentPlacement>)placements.Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber).ToArray());
}

file sealed class ValidationStubDocumentRepository(IReadOnlyCollection<SubmissionDocument> documents) : IDocumentRepository
{
    public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(documents.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(documents);
}

file sealed class ValidationStubAuditLogService : IAuditLogService
{
    public Task<AuditLogDto> CreateAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AuditLogDto(Guid.NewGuid(), request.EntityType, request.EntityId, request.Action, request.Actor, request.Details, DateTime.UtcNow));

    public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyCollection<AuditLogDto>)Array.Empty<AuditLogDto>());
}

file sealed class ValidationStubProfileProvider : IValidationProfileProvider
{
    public string ProfileName => "default-v1";
    public ValidationMode Mode => ValidationMode.Strict;
}
