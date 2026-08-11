using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;
using RATools.Application.Auditing.Requests;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

[Trait("Category", "PublishJobReliability")]
public sealed class PublishJobTerminalPersistenceTests
{
    [Fact]
    public async Task CreateAsync_ValidationCancellationPersistsFailedWithIndependentCleanupToken()
    {
        using var executionCts = new CancellationTokenSource();
        var repository = new SnapshotPublishJobRepository();
        var service = CreateService(
            repository,
            validationService: new CancelingValidationService(executionCts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(
            NewRequest(),
            executionCts.Token));

        var persisted = Assert.Single(await repository.ListAsync());
        Assert.Equal(PublishJobStatus.Failed, persisted.Status);
        Assert.Contains("canceled or timed out", persisted.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(repository.UpdateObservations, update =>
            update.Status == PublishJobStatus.Failed && !update.CancellationRequested);
    }

    [Fact]
    public async Task EnqueueExecutionAsync_SignalFailureLeavesDurablePendingJobDiscoverable()
    {
        using var enqueueCts = new CancellationTokenSource();
        var repository = new SnapshotPublishJobRepository();
        var service = CreateService(repository, queue: new CancelingPublishJobQueue(enqueueCts));

        var result = await service.EnqueueExecutionAsync(
            NewRequest(),
            enqueueCts.Token);

        var persisted = Assert.Single(await repository.ListAsync());
        Assert.Equal(PublishJobStatus.Pending.ToString(), result.Status);
        Assert.Equal(PublishJobStatus.Pending, persisted.Status);
        Assert.Null(persisted.FailureReason);
    }

    [Fact]
    public async Task CreateAsync_ReadinessCancellationPersistsFailedWithIndependentCleanupToken()
    {
        using var executionCts = new CancellationTokenSource();
        var repository = new SnapshotPublishJobRepository();
        var service = CreateService(
            repository,
            publishReadinessService: new CancelingReadinessService(executionCts));

        var result = await service.CreateAsync(NewRequest(), executionCts.Token);

        Assert.Equal(PublishJobStatus.Failed.ToString(), result.Status);
        var persisted = Assert.Single(await repository.ListAsync());
        Assert.Equal(PublishJobStatus.Failed, persisted.Status);
        Assert.Contains(repository.UpdateObservations, update =>
            update.Status == PublishJobStatus.Failed && !update.CancellationRequested);
    }

    [Fact]
    public async Task CreateAsync_WriterCancellationPersistsFailedWithIndependentCleanupToken()
    {
        using var executionCts = new CancellationTokenSource();
        var repository = new SnapshotPublishJobRepository();
        var service = CreateService(
            repository,
            backboneService: new CancelingBackboneService(executionCts));

        var result = await service.CreateAsync(NewRequest(), executionCts.Token);

        Assert.Equal(PublishJobStatus.Failed.ToString(), result.Status);
        var persisted = Assert.Single(await repository.ListAsync());
        Assert.Equal(PublishJobStatus.Failed, persisted.Status);
        Assert.Contains(repository.UpdateObservations, update =>
            update.Status == PublishJobStatus.Failed && !update.CancellationRequested);
    }

    [Fact]
    public async Task CreateAsync_CompletedAuditCancellationDoesNotDemotePersistedJob()
    {
        using var executionCts = new CancellationTokenSource();
        var repository = new SnapshotPublishJobRepository();
        var auditService = new StubAuditLogService((request, _) =>
        {
            if (!string.Equals(request.Action, "Completed", StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            executionCts.Cancel();
            return Task.FromCanceled(executionCts.Token);
        });
        var service = CreateService(repository, auditLogService: auditService);

        var result = await service.CreateAsync(NewRequest(), executionCts.Token);

        Assert.Equal(PublishJobStatus.Completed.ToString(), result.Status);
        var persisted = Assert.Single(await repository.ListAsync());
        Assert.Equal(PublishJobStatus.Completed, persisted.Status);
        Assert.DoesNotContain(repository.UpdateObservations, update => update.Status == PublishJobStatus.Failed);
        Assert.Contains(repository.UpdateObservations, update =>
            update.Status == PublishJobStatus.Completed && !update.CancellationRequested);
    }

    [Fact]
    public async Task CreateAsync_AuditFailureDoesNotChangeCompletedTerminalState()
    {
        var repository = new SnapshotPublishJobRepository();
        var auditService = new StubAuditLogService((_, _) =>
            Task.FromException(new InvalidOperationException("audit unavailable")));
        var service = CreateService(repository, auditLogService: auditService);

        var result = await service.CreateAsync(NewRequest());

        Assert.Equal(PublishJobStatus.Completed.ToString(), result.Status);
        Assert.Equal(PublishJobStatus.Completed, Assert.Single(await repository.ListAsync()).Status);
    }

    [Fact]
    public async Task CreateAsync_WriterFailurePersistsFailedAndAllowsSameSequenceRetry()
    {
        var applicationId = Guid.NewGuid();
        var request = new CreatePublishJobRequest(applicationId, "0001");
        var repository = new SnapshotPublishJobRepository();
        var failingService = CreateService(
            repository,
            backboneService: new ThrowingBackboneService());

        var failed = await failingService.CreateAsync(request);

        Assert.Equal(PublishJobStatus.Failed.ToString(), failed.Status);
        var retryService = CreateService(repository);
        var completed = await retryService.CreateAsync(request);

        Assert.Equal(PublishJobStatus.Completed.ToString(), completed.Status);
        var persisted = await repository.ListAsync();
        Assert.Equal(2, persisted.Count);
        Assert.Contains(persisted, job => job.Id == failed.Id && job.Status == PublishJobStatus.Failed);
        Assert.Contains(persisted, job => job.Id == completed.Id && job.Status == PublishJobStatus.Completed);
    }

    [Fact]
    public async Task CreateAsync_RepositoryFailureWhileMarkingRunningFallsBackToFailedTerminalState()
    {
        var repository = new SnapshotPublishJobRepository
        {
            UpdateFailure = (job, attempt) =>
                attempt == 1 && job.Status == PublishJobStatus.Running
                    ? new InvalidOperationException("running update failed")
                    : null
        };
        var service = CreateService(repository);

        var result = await service.CreateAsync(NewRequest());

        Assert.Equal(PublishJobStatus.Failed.ToString(), result.Status);
        Assert.Equal(PublishJobStatus.Failed, Assert.Single(await repository.ListAsync()).Status);
        Assert.Contains(repository.UpdateObservations, update =>
            update.Status == PublishJobStatus.Failed && !update.CancellationRequested);
    }

    [Fact]
    public async Task CreateAsync_TransientCompletedRepositoryFailureRetriesWithoutDemotingJob()
    {
        var completedAttempts = 0;
        var repository = new SnapshotPublishJobRepository
        {
            UpdateFailure = (job, _) =>
            {
                if (job.Status != PublishJobStatus.Completed || ++completedAttempts != 1)
                {
                    return null;
                }

                return new InvalidOperationException("terminal update failed once");
            }
        };
        var service = CreateService(repository);

        var result = await service.CreateAsync(NewRequest());

        Assert.Equal(PublishJobStatus.Completed.ToString(), result.Status);
        Assert.Equal(PublishJobStatus.Completed, Assert.Single(await repository.ListAsync()).Status);
        Assert.Equal(2, repository.UpdateObservations.Count(update => update.Status == PublishJobStatus.Completed));
        Assert.DoesNotContain(repository.UpdateObservations, update => update.Status == PublishJobStatus.Failed);
    }

    [Fact]
    public async Task BackgroundService_ExecutionTimeoutPersistsFailedTerminalState()
    {
        await using var harness = await BackgroundHarness.CreateAsync(TimeSpan.FromMilliseconds(100));

        await harness.StartAsync();
        await harness.Backbone.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var persisted = await harness.WaitForTerminalAsync();

        Assert.Equal(PublishJobStatus.Failed, persisted.Status);
        Assert.Contains("canceled or timed out", persisted.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(harness.Repository.UpdateObservations, update =>
            update.Status == PublishJobStatus.Failed && !update.CancellationRequested);
    }

    [Fact]
    public async Task BackgroundService_HostStoppingPersistsFailedTerminalState()
    {
        await using var harness = await BackgroundHarness.CreateAsync(TimeSpan.FromMinutes(15));

        await harness.StartAsync();
        await harness.Backbone.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.StopAsync();
        var persisted = await harness.WaitForTerminalAsync();

        Assert.Equal(PublishJobStatus.Failed, persisted.Status);
        Assert.Contains("canceled or timed out", persisted.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    private static CreatePublishJobRequest NewRequest()
        => new(Guid.NewGuid(), "0001");

    private static PublishJobService CreateService(
        IPublishJobRepository repository,
        IBackboneService? backboneService = null,
        ISequenceValidationService? validationService = null,
        IPublishReadinessService? publishReadinessService = null,
        IAuditLogService? auditLogService = null,
        IPublishJobQueue? queue = null)
    {
        var artifactStore = new StubPublishArtifactStore();
        return new PublishJobService(
            repository,
            backboneService ?? new PassingBackboneService(),
            validationService ?? new PassingValidationService(),
            publishReadinessService ?? new PassingReadinessService(),
            auditLogService ?? new StubAuditLogService(),
            new PublishArtifactResolver(artifactStore),
            new PublishReportStore(artifactStore),
            new PublishOutputVerifier(),
            queue ?? new FakePublishJobQueue(),
            NullLogger<PublishJobService>.Instance);
    }

    private static ValidationReportDto BuildValidationReport(ValidateSequenceRequest request)
        => new(
            request.ApplicationId,
            request.SequenceNumber,
            "test-profile",
            true,
            [],
            [],
            []);

    private static PublishReadinessReportDto BuildReadinessReport(
        ValidateSequenceRequest request,
        ValidationReportDto validationReport)
        => new(
            request.ApplicationId,
            request.SequenceNumber,
            true,
            "Ready",
            0,
            0,
            validationReport,
            [],
            [],
            []);

    private sealed class PassingValidationService : ISequenceValidationService
    {
        public Task<ValidationReportDto> ValidateAsync(
            ValidateSequenceRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(BuildValidationReport(request));
    }

    private sealed class CancelingValidationService(CancellationTokenSource cancellationSource) : ISequenceValidationService
    {
        public Task<ValidationReportDto> ValidateAsync(
            ValidateSequenceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationSource.Cancel();
            return Task.FromCanceled<ValidationReportDto>(cancellationSource.Token);
        }
    }

    private sealed class PassingReadinessService : IPublishReadinessService
    {
        public Task<PublishReadinessReportDto> GetAsync(
            ValidateSequenceRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(BuildReadinessReport(request, BuildValidationReport(request)));

        public Task<PublishReadinessReportDto> GetAsync(
            ValidateSequenceRequest request,
            ValidationReportDto validationReport,
            CancellationToken cancellationToken = default)
            => Task.FromResult(BuildReadinessReport(request, validationReport));
    }

    private sealed class CancelingReadinessService(CancellationTokenSource cancellationSource) : IPublishReadinessService
    {
        public Task<PublishReadinessReportDto> GetAsync(
            ValidateSequenceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationSource.Cancel();
            return Task.FromCanceled<PublishReadinessReportDto>(cancellationSource.Token);
        }

        public Task<PublishReadinessReportDto> GetAsync(
            ValidateSequenceRequest request,
            ValidationReportDto validationReport,
            CancellationToken cancellationToken = default)
            => GetAsync(request, cancellationToken);
    }

    private sealed class PassingBackboneService : IBackboneService
    {
        public Task<GeneratedBackboneDto> GenerateAsync(
            GenerateBackboneRequest request,
            CancellationToken cancellationToken = default)
        {
            var root = Path.Combine(Path.GetTempPath(), "ratools-terminal-tests", request.PublishJobId.ToString("N"));
            return Task.FromResult(new GeneratedBackboneDto(
                request.ApplicationId,
                request.SequenceNumber,
                "index.xml",
                Path.Combine(root, "index.xml"),
                Path.Combine(root, request.ReportFileName),
                Path.Combine(root, request.PackageFileName),
                "<ectd />"));
        }
    }

    private sealed class CancelingBackboneService(CancellationTokenSource cancellationSource) : IBackboneService
    {
        public Task<GeneratedBackboneDto> GenerateAsync(
            GenerateBackboneRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationSource.Cancel();
            return Task.FromCanceled<GeneratedBackboneDto>(cancellationSource.Token);
        }
    }

    private sealed class ThrowingBackboneService : IBackboneService
    {
        public Task<GeneratedBackboneDto> GenerateAsync(
            GenerateBackboneRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromException<GeneratedBackboneDto>(new InvalidOperationException("writer failed"));
    }

    private sealed class DelayingBackboneService : IBackboneService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GeneratedBackboneDto> GenerateAsync(
            GenerateBackboneRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after an infinite delay.");
        }
    }

    private sealed class StubAuditLogService(
        Func<CreateAuditLogRequest, CancellationToken, Task>? createBehavior = null) : IAuditLogService
    {
        public async Task<AuditLogDto> WriteSystemEventAsync(
            CreateAuditLogRequest request,
            CancellationToken cancellationToken = default)
        {
            if (createBehavior is not null)
            {
                await createBehavior(request, cancellationToken);
            }

            return new AuditLogDto(
                Guid.NewGuid(),
                request.EntityType,
                request.EntityId,
                request.Action,
                "system",
                request.Details,
                DateTime.UtcNow);
        }

        public Task<IReadOnlyCollection<AuditLogDto>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>([]);

        public Task<IReadOnlyCollection<AuditLogDto>> ListByEntitiesAsync(
            IReadOnlyCollection<(string EntityType, string EntityId)> entities,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AuditLogDto>>([]);

        public Task<AuditLogPageDto> QueryAsync(
            AuditLogQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditLogPageDto(query.Page, query.PageSize, 0, []));
    }

    private sealed class StubPublishArtifactStore : IPublishArtifactStore
    {
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task WriteAllTextAsync(
            string path,
            string content,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<PublishArtifactDirectoryStats> GetDirectoryStatsAsync(
            string directoryPath,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PublishArtifactDirectoryStats(0, 0));
    }

    private sealed record UpdateObservation(PublishJobStatus Status, bool CancellationRequested);

    private sealed class SnapshotPublishJobRepository : IPublishJobRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, PublishJob> _items = [];
        private readonly List<UpdateObservation> _updateObservations = [];
        private int _updateAttempt;

        public Func<PublishJob, int, Exception?>? UpdateFailure { get; init; }

        public IReadOnlyCollection<UpdateObservation> UpdateObservations
        {
            get
            {
                lock (_gate)
                {
                    return _updateObservations.ToArray();
                }
            }
        }

        public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (IsActive(job.Status) && _items.Values.Any(existing =>
                        existing.ApplicationId == job.ApplicationId
                        && existing.SequenceNumber == job.SequenceNumber
                        && IsActive(existing.Status)))
                {
                    throw new PublishJobAlreadyInProgressException("An active publish job already exists.");
                }

                _items[job.Id] = Clone(job);
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default)
        {
            Exception? failure;
            lock (_gate)
            {
                var attempt = ++_updateAttempt;
                _updateObservations.Add(new UpdateObservation(job.Status, cancellationToken.IsCancellationRequested));
                failure = UpdateFailure?.Invoke(job, attempt);
                if (failure is null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_items.ContainsKey(job.Id))
                    {
                        _items[job.Id] = Clone(job);
                    }
                }
            }

            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }

        public Task<bool> UpdateHistorySummaryAsync(
            Guid jobId,
            int expectedAttemptCount,
            PublishJobHistorySummary summary,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_items.TryGetValue(id, out var job) ? Clone(job) : null);
            }
        }

        public Task<PublishJobLease?> TryClaimNextAsync(
            string owner,
            DateTime nowUtc,
            TimeSpan leaseDuration,
            int maxAttempts,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var job = _items.Values
                    .Where(item => item.Status == PublishJobStatus.Pending
                        && item.NextAttemptUtc <= nowUtc
                        && item.AttemptCount < maxAttempts)
                    .OrderBy(item => item.CreatedUtc)
                    .FirstOrDefault();
                if (job is null)
                {
                    return Task.FromResult<PublishJobLease?>(null);
                }

                var token = job.Claim(owner, nowUtc, leaseDuration);
                _items[job.Id] = Clone(job);
                return Task.FromResult<PublishJobLease?>(
                    new PublishJobLease(Clone(job), token, owner, nowUtc.Add(leaseDuration)));
            }
        }

        public Task<bool> RenewLeaseAsync(
            Guid jobId,
            Guid leaseToken,
            string owner,
            DateTime nowUtc,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (!_items.TryGetValue(jobId, out var job)
                    || job.LeaseToken != leaseToken
                    || job.LeaseOwner != owner
                    || job.LeaseExpiresUtc <= nowUtc)
                {
                    return Task.FromResult(false);
                }

                job.RenewLease(leaseToken, owner, nowUtc, leaseDuration);
                _items[jobId] = Clone(job);
                return Task.FromResult(true);
            }
        }

        public async Task<bool> UpdateLeasedAsync(
            PublishJob job,
            Guid leaseToken,
            string owner,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (!_items.TryGetValue(job.Id, out var stored)
                    || stored.LeaseToken != leaseToken
                    || stored.LeaseOwner != owner
                    || stored.LeaseExpiresUtc <= nowUtc)
                {
                    return false;
                }
            }

            await UpdateAsync(job, cancellationToken);
            return true;
        }

        public Task<PublishJobRetryResult> RetryOrFailLeasedAsync(
            Guid jobId,
            Guid leaseToken,
            string owner,
            DateTime nowUtc,
            DateTime nextAttemptUtc,
            int maxAttempts,
            string failureReason,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (!_items.TryGetValue(jobId, out var job)
                    || job.LeaseToken != leaseToken
                    || job.LeaseOwner != owner
                    || job.LeaseExpiresUtc <= nowUtc)
                {
                    return Task.FromResult(new PublishJobRetryResult(PublishJobRetryDisposition.LeaseLost, null));
                }

                if (job.AttemptCount >= maxAttempts)
                {
                    job.MarkFailed(failureReason);
                    _updateObservations.Add(new UpdateObservation(job.Status, cancellationToken.IsCancellationRequested));
                    _items[jobId] = Clone(job);
                    return Task.FromResult(new PublishJobRetryResult(PublishJobRetryDisposition.Failed, Clone(job)));
                }

                job.ScheduleRetry(leaseToken, owner, failureReason, nextAttemptUtc);
                _updateObservations.Add(new UpdateObservation(job.Status, cancellationToken.IsCancellationRequested));
                _items[jobId] = Clone(job);
                return Task.FromResult(new PublishJobRetryResult(PublishJobRetryDisposition.RetryScheduled, Clone(job)));
            }
        }

        public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyCollection<PublishJob>>(
                    _items.Values.Select(Clone).OrderBy(job => job.CreatedUtc).ToArray());
            }
        }

        public Task<IReadOnlyCollection<PublishJob>> ListActiveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyCollection<PublishJob>>(
                    _items.Values.Where(job => IsActive(job.Status)).Select(Clone).ToArray());
            }
        }

        public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(
            PublishJobHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var filtered = _items.Values
                    .Where(job => job.ApplicationId == query.ApplicationId)
                    .Where(job => string.IsNullOrWhiteSpace(query.SequenceNumber)
                                  || job.SequenceNumber == query.SequenceNumber)
                    .Where(job => string.IsNullOrWhiteSpace(query.Status)
                                  || job.Status.ToString().Equals(query.Status, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(job => job.CreatedUtc)
                    .ToArray();
                var pageItems = filtered
                    .Skip((Math.Max(1, query.Page) - 1) * Math.Max(1, query.PageSize))
                    .Take(Math.Max(1, query.PageSize))
                    .Select(Clone)
                    .ToArray();

                return Task.FromResult(new PublishJobHistoryQueryResult(
                    pageItems,
                    filtered.Length,
                    filtered.Count(job => job.Status == PublishJobStatus.Completed),
                    filtered.Count(job => job.Status == PublishJobStatus.Failed),
                    filtered.Count(job => job.Status == PublishJobStatus.Running)));
            }
        }

        public Task DeleteByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                foreach (var id in _items.Values.Where(job => job.ApplicationId == applicationId).Select(job => job.Id).ToArray())
                {
                    _items.Remove(id);
                }
            }

            return Task.CompletedTask;
        }

        public Task DeleteBySequenceAsync(
            Guid applicationId,
            string sequenceNumber,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                foreach (var id in _items.Values
                             .Where(job => job.ApplicationId == applicationId && job.SequenceNumber == sequenceNumber)
                             .Select(job => job.Id)
                             .ToArray())
                {
                    _items.Remove(id);
                }
            }

            return Task.CompletedTask;
        }

        private static bool IsActive(PublishJobStatus status)
            => status is PublishJobStatus.Pending or PublishJobStatus.Running;

        private static PublishJob Clone(PublishJob job)
            => PublishJob.Rehydrate(
                job.Id,
                job.ApplicationId,
                job.SequenceNumber,
                job.Status,
                job.OutputPath,
                job.PackagePath,
                job.CreatedUtc,
                job.CompletedUtc,
                job.FailureReason,
                job.IdempotencyKey,
                job.AttemptCount,
                job.NextAttemptUtc,
                job.LeaseOwner,
                job.LeaseToken,
                job.LeaseExpiresUtc,
                job.LastHeartbeatUtc);
    }

    private sealed class CancelingPublishJobQueue(CancellationTokenSource cancellationTokenSource) : IPublishJobQueue
    {
        public ValueTask EnqueueAsync(QueuedPublishJob job, CancellationToken cancellationToken = default)
        {
            cancellationTokenSource.Cancel();
            return ValueTask.FromCanceled(cancellationTokenSource.Token);
        }

        public ValueTask WaitForWorkAsync(TimeSpan maximumDelay, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class BackgroundHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly PublishJobBackgroundService _backgroundService;
        private bool _stopped;

        private BackgroundHarness(
            ServiceProvider serviceProvider,
            PublishJobBackgroundService backgroundService,
            SnapshotPublishJobRepository repository,
            DelayingBackboneService backbone,
            Guid jobId)
        {
            _serviceProvider = serviceProvider;
            _backgroundService = backgroundService;
            Repository = repository;
            Backbone = backbone;
            JobId = jobId;
        }

        public SnapshotPublishJobRepository Repository { get; }

        public DelayingBackboneService Backbone { get; }

        public Guid JobId { get; }

        public static async Task<BackgroundHarness> CreateAsync(TimeSpan executionTimeout)
        {
            var repository = new SnapshotPublishJobRepository();
            var backbone = new DelayingBackboneService();
            var queue = new ChannelPublishJobQueue();
            var service = CreateService(repository, backboneService: backbone, queue: queue);
            var request = NewRequest();
            var job = await service.EnqueueExecutionAsync(request);

            var services = new ServiceCollection();
            services.AddSingleton<IPublishJobRepository>(repository);
            services.AddSingleton<IPublishJobService>(service);
            var provider = services.BuildServiceProvider();
            var background = new PublishJobBackgroundService(
                queue,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new PublishJobExecutionOptions
                {
                    ExecutionTimeout = executionTimeout,
                    PollInterval = TimeSpan.FromMilliseconds(10),
                    LeaseDuration = TimeSpan.FromSeconds(1),
                    HeartbeatInterval = TimeSpan.FromMilliseconds(50),
                    RetryDelay = TimeSpan.Zero,
                    MaxAttempts = 1
                }),
                NullLogger<PublishJobBackgroundService>.Instance);

            return new BackgroundHarness(provider, background, repository, backbone, job.Id);
        }

        public Task StartAsync()
            => _backgroundService.StartAsync(CancellationToken.None);

        public async Task StopAsync()
        {
            if (_stopped)
            {
                return;
            }

            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _backgroundService.StopAsync(stopCts.Token);
            _stopped = true;
        }

        public async Task<PublishJob> WaitForTerminalAsync()
        {
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                waitCts.Token.ThrowIfCancellationRequested();
                var job = await Repository.GetAsync(JobId, waitCts.Token);
                if (job?.Status is PublishJobStatus.Completed or PublishJobStatus.Failed)
                {
                    return job;
                }

                await Task.Delay(10, waitCts.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _backgroundService.Dispose();
            await _serviceProvider.DisposeAsync();
        }
    }
}
