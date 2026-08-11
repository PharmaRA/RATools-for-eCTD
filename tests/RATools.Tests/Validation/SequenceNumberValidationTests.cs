using Microsoft.Extensions.Logging.Abstractions;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

using RATools.Tests.TestDoubles;

namespace RATools.Tests.Validation;

public sealed class SequenceNumberValidationTests
{
    [Fact]
    public async Task ValidateAsync_ReportsInvalidSequenceNumberFormat()
    {
        var applicationId = Guid.NewGuid();
        var service = CreateService(applicationId, sequenceNumbers: ["abc"]);

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "abc"));

        var issue = Assert.Single(report.Issues, x => x.Code == "SEQUENCE_NUMBER_FORMAT_INVALID");
        Assert.Equal("Error", issue.Severity);
        Assert.False(report.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsFourDigitSequenceNumber()
    {
        var applicationId = Guid.NewGuid();
        var service = CreateService(applicationId, sequenceNumbers: ["0000"]);

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0000"));

        Assert.DoesNotContain(report.Issues, x => x.Code == "SEQUENCE_NUMBER_FORMAT_INVALID");
    }

    [Fact]
    public async Task ValidateAsync_WarnsOnSequenceGapInStrictMode()
    {
        var applicationId = Guid.NewGuid();
        var service = CreateService(applicationId, sequenceNumbers: ["0000", "0003"], strict: true);

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0003"));

        var issue = Assert.Single(report.Issues, x => x.Code == "SEQUENCE_GAP_DETECTED");
        Assert.Equal("Warning", issue.Severity);
        Assert.Contains("0000", issue.Message, StringComparison.Ordinal);
        Assert.Contains("0003", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotWarnForContiguousSequencesInStrictMode()
    {
        var applicationId = Guid.NewGuid();
        var service = CreateService(applicationId, sequenceNumbers: ["0000", "0001"], strict: true);

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0001"));

        Assert.DoesNotContain(report.Issues, x => x.Code == "SEQUENCE_GAP_DETECTED");
    }

    [Fact]
    public async Task ValidateAsync_DoesNotWarnAboutGapsInRelaxedMode()
    {
        var applicationId = Guid.NewGuid();
        var service = CreateService(applicationId, sequenceNumbers: ["0000", "0005"], strict: false);

        var report = await service.ValidateAsync(new ValidateSequenceRequest(applicationId, "0005"));

        Assert.DoesNotContain(report.Issues, x => x.Code == "SEQUENCE_GAP_DETECTED");
    }

    private static SequenceValidationService CreateService(Guid applicationId, string[] sequenceNumbers, bool strict = false)
    {
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            sequenceNumbers.Select(x => SubmissionSequence.Rehydrate(x, "original", "Original", DateTime.UtcNow)).ToArray(),
            Path.GetTempPath(),
            EctdTemplateRegistry.DefaultTemplateKey);

        return new SequenceValidationService(
            new StubApplicationRepository(application),
            new StubDocumentPlacementRepository(),
            new StubDocumentRepository(),
            new StubAuditLogService(),
            strict ? new StrictProfileProvider() : new RelaxedProfileProvider(),
            NullLogger<SequenceValidationService>.Instance,
            PermissiveDocumentStorageBoundary.Instance);
    }

    private sealed class StubApplicationRepository(SubmissionApplication application) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(SubmissionApplication entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<SubmissionApplication?>(application);
        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([application]);
    }

    private sealed class StubDocumentPlacementRepository : IDocumentPlacementRepository
    {
        public Task AddAsync(DocumentPlacement entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateAsync(DocumentPlacement entity, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<DocumentPlacement?>(null);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
    }

    private sealed class StubDocumentRepository : IDocumentRepository
    {
        public Task AddAsync(SubmissionDocument entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateAsync(SubmissionDocument entity, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<SubmissionDocument?>(null);
        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionDocument>>([]);
        public Task<IReadOnlyCollection<SubmissionDocument>> ListByIdsPreferScopedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionDocument>>([]);
    }

    private sealed class StubAuditLogService : IAuditLogService
    {
        public Task<AuditLogDto> WriteSystemEventAsync(CreateAuditLogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditLogDto(Guid.NewGuid(), request.EntityType, request.EntityId, request.Action, "system", request.Details, DateTime.UtcNow));

        public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>([]);

        public Task<IReadOnlyCollection<AuditLogDto>> ListByEntitiesAsync(
            IReadOnlyCollection<(string EntityType, string EntityId)> entities,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>([]);

        public Task<AuditLogPageDto> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditLogPageDto(query.Page, query.PageSize, 0, []));
    }

    private sealed class StrictProfileProvider : IValidationProfileProvider
    {
        public string ProfileName => SectionDictionaryProfiles.CanonicalUsProfileName;
        public ValidationMode Mode => ValidationMode.Strict;
    }

    private sealed class RelaxedProfileProvider : IValidationProfileProvider
    {
        public string ProfileName => SectionDictionaryProfiles.CanonicalUsProfileName;
        public ValidationMode Mode => ValidationMode.Relaxed;
    }
}
