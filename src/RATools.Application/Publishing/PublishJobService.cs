using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Application.Validation.Dtos;
using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

public sealed class PublishJobService(
    IPublishJobRepository repository,
    IBackboneService backboneService,
    ISequenceValidationService validationService,
    IPublishReadinessService publishReadinessService,
    IAuditLogService auditLogService,
    PublishArtifactResolver artifactResolver,
    PublishReportStore reportStore,
    PublishOutputVerifier publishOutputVerifier,
    IPublishJobQueue publishJobQueue,
    ILogger<PublishJobService> logger) : IPublishJobService
{
    private const string PublishExecutionReportVersion = "1.1";
    private static readonly TimeSpan TerminalCleanupTimeout = TimeSpan.FromSeconds(30);

    // 保留给现有应用层测试与内部调用方的兼容入口；HTTP API 不再暴露该同步语义。
    public async Task<PublishJobDto> CreateAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteInternalAsync(request, cancellationToken);
        return result.Job.ToDto();
    }

    public async Task<PublishExecutionReportDto> ExecuteAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await ExecuteInternalAsync(request, cancellationToken);
        stopwatch.Stop();

        return await BuildAndPersistReportAsync(request, result, stopwatch.ElapsedMilliseconds, cancellationToken);
    }

    // 后台执行入口：在一个已创建的 Pending 作业上运行发布流程并生成报告。
    // 防重已在 CreatePendingJobAsync 阶段保证，此处不再重复创建作业。
    public async Task<PublishExecutionReportDto> ExecuteQueuedAsync(
        Guid jobId,
        CreatePublishJobRequest request,
        CancellationToken cancellationToken = default)
    {
        using var lookupCts = new CancellationTokenSource(TerminalCleanupTimeout);
        var job = await repository.GetAsync(jobId, lookupCts.Token)
            ?? throw new InvalidOperationException($"Publish job {jobId} was not found for queued execution.");

        var stopwatch = Stopwatch.StartNew();
        var result = await RunPipelineAsync(job, request, cancellationToken);
        stopwatch.Stop();

        return await BuildAndPersistReportAsync(request, result, stopwatch.ElapsedMilliseconds, cancellationToken);
    }

    public async Task<PublishExecutionReportDto> ExecuteClaimedAsync(
        PublishJobLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.Job.Status != PublishJobStatus.Running
            || lease.Job.LeaseToken != lease.Token
            || !string.Equals(lease.Job.LeaseOwner, lease.Owner, StringComparison.Ordinal))
        {
            throw new PublishJobLeaseLostException(lease.Job.Id);
        }

        var request = new CreatePublishJobRequest(
            lease.Job.ApplicationId,
            lease.Job.SequenceNumber,
            lease.Job.IdempotencyKey);
        var stopwatch = Stopwatch.StartNew();
        var result = await RunPipelineAsync(lease.Job, request, cancellationToken, lease);
        stopwatch.Stop();

        return await BuildAndPersistReportAsync(request, result, stopwatch.ElapsedMilliseconds, cancellationToken);
    }

    public async Task<PublishJobDto> EnqueueExecutionAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
    {
        var enqueueResult = await CreateQueuedPendingJobAsync(request, cancellationToken);
        if (enqueueResult.Created)
        {
            // 数据库 Pending 行是队列事实；Channel 仅负责降低 worker 轮询延迟。
            try
            {
                await publishJobQueue.EnqueueAsync(
                    new QueuedPublishJob(enqueueResult.Job.Id, request),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                PublishPipelineLog.QueueSignalFailed(logger, exception, enqueueResult.Job.Id);
            }
        }

        return enqueueResult.Job.ToDto();
    }

    private async Task<PublishExecutionReportDto> BuildAndPersistReportAsync(
        CreatePublishJobRequest request,
        (PublishJob Job, ValidationReportDto ValidationReport, PublishReadinessReportDto? PublishReadiness, string? Message, string? ReportPath) result,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        var jobDto = result.Job.ToDto();
        var issueSummary = PublishValidationIssueReportSummary.Create(result.ValidationReport.Issues);
        var auditSummary = await BuildAuditSummaryAsync(jobDto, request.SequenceNumber, cancellationToken);

        var report = new PublishExecutionReportDto(
            PublishExecutionReportVersion,
            request.ApplicationId,
            request.SequenceNumber,
            result.ValidationReport.ValidationProfile,
            result.ReportPath,
            result.ValidationReport,
            jobDto,
            elapsedMilliseconds,
            null,
            null,
            result.PublishReadiness,
            null,
            auditSummary,
            issueSummary.ErrorCount,
            issueSummary.WarningCount,
            issueSummary.WarningSummary,
            result.Job.Status == PublishJobStatus.Completed,
            result.Message);

        report = report with { ArtifactSummary = await artifactResolver.BuildArtifactSummaryAsync(jobDto, cancellationToken) };

        if (!string.IsNullOrWhiteSpace(report.ReportPath) && !string.IsNullOrWhiteSpace(jobDto.PackagePath))
        {
            await reportStore.WriteAsync(report, cancellationToken);
        }

        var integrityVerification = await publishOutputVerifier.VerifyAsync(jobDto.OutputPath, result.ReportPath, jobDto.PackagePath, cancellationToken);
        report = report with
        {
            IntegritySummary = integrityVerification.Summary,
            IntegrityEvidence = integrityVerification.Evidence
        };

        if (!string.IsNullOrWhiteSpace(report.ReportPath) && !string.IsNullOrWhiteSpace(jobDto.PackagePath))
        {
            await reportStore.WriteAsync(report, cancellationToken);
        }

        return report;
    }

    public async Task<PublishExecutionReportDto?> GetExecutionReportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        return await reportStore.ReadAsync(job, cancellationToken);
    }

    public async Task<PublishArtifactsDto?> GetArtifactsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        return await artifactResolver.BuildArtifactsAsync(job, cancellationToken);
    }

    public async Task<PublishArtifactDownloadDto?> GetArtifactDownloadAsync(Guid id, string artifactName, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.Status != PublishJobStatus.Completed)
        {
            throw new PublishJobNotReadyException($"Publish job {id} is in status '{job.Status}' and artifacts are not available yet.");
        }

        var artifact = await artifactResolver.ResolveAsync(job, artifactName, cancellationToken);
        if (artifact is null)
        {
            throw new PublishArtifactNotSupportedException($"Artifact '{artifactName}' is not supported.");
        }

        if (!artifact.Exists)
        {
            throw new PublishJobReportUnavailableException($"Artifact '{artifactName}' for job {id} was not found.");
        }

        await TryWriteAuditAsync(
            entityType: "PublishJobArtifact",
            entityId: $"{id}:{artifact.Name}",
            action: "Downloaded",
            details: $"Path={artifact.Path}; ContentType={artifact.ContentType}",
            cancellationToken);

        return new PublishArtifactDownloadDto(
            artifact.Name,
            Path.GetFileName(artifact.Path!),
            artifact.Path!,
            artifact.ContentType);
    }

    private async Task<PublishAuditSummaryDto?> BuildAuditSummaryAsync(
        PublishJobDto publishJob,
        string sequenceNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            // 只取当前作业与当前序列的审计条目；审计表只增，全表拉取会随时间线性劣化。
            var relatedAuditLogs = await auditLogService.ListByEntitiesAsync(
                [
                    ("PublishJob", publishJob.Id.ToString()),
                    ("SequenceValidation", $"{publishJob.ApplicationId}:{sequenceNumber}"),
                ],
                cancellationToken);
            return PublishAuditSummaryBuilder.Create(relatedAuditLogs, publishJob, sequenceNumber);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishPipelineLog.AuditSummaryFailed(logger, exception, publishJob.Id);
            return null;
        }
    }

    private async Task<(PublishJob Job, ValidationReportDto ValidationReport, PublishReadinessReportDto? PublishReadiness, string? Message, string? ReportPath)> ExecuteInternalAsync(
        CreatePublishJobRequest request,
        CancellationToken cancellationToken)
    {
        var job = await CreatePendingJobAsync(request, cancellationToken);
        return await RunPipelineAsync(job, request, cancellationToken);
    }

    // 创建处于 Pending 的作业并持久化。防重的事实来源是 repository.AddAsync
    // （活动作业唯一约束/守卫）；EnsureNoActivePublishAsync 仅作 best-effort 友好提示。
    private async Task<PublishJob> CreatePendingJobAsync(
        CreatePublishJobRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureNoActivePublishAsync(request, cancellationToken);

        var job = new PublishJob(request.ApplicationId, request.SequenceNumber);
        await repository.AddAsync(job, cancellationToken);
        PublishPipelineLog.JobCreated(logger, job.Id, request.ApplicationId, request.SequenceNumber);
        await TryWriteAuditAsync(
            entityType: "PublishJob",
            entityId: job.Id.ToString(),
            action: "Created",
            details: $"Publish job created for application {request.ApplicationId}, sequence {request.SequenceNumber}.",
            cancellationToken,
            ignoreCancellation: true);

        return job;
    }

    private async Task<PublishJobEnqueueResult> CreateQueuedPendingJobAsync(
        CreatePublishJobRequest request,
        CancellationToken cancellationToken)
    {
        var candidate = new PublishJob(request.ApplicationId, request.SequenceNumber, request.IdempotencyKey);
        var existing = await repository.GetByIdempotencyKeyAsync(candidate.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            EnsureIdempotencyRequestMatches(existing, request);
            return new PublishJobEnqueueResult(existing, Created: false);
        }

        var result = await repository.AddOrGetByIdempotencyKeyAsync(candidate, cancellationToken);
        EnsureIdempotencyRequestMatches(result.Job, request);
        if (!result.Created)
        {
            return result;
        }

        PublishPipelineLog.JobCreated(logger, candidate.Id, request.ApplicationId, request.SequenceNumber);
        await TryWriteAuditAsync(
            entityType: "PublishJob",
            entityId: candidate.Id.ToString(),
            action: "Created",
            details: $"Publish job created for application {request.ApplicationId}, sequence {request.SequenceNumber}.",
            cancellationToken,
            ignoreCancellation: true);
        return result;
    }

    private static void EnsureIdempotencyRequestMatches(PublishJob job, CreatePublishJobRequest request)
    {
        if (job.ApplicationId != request.ApplicationId
            || !string.Equals(job.SequenceNumber, request.SequenceNumber.Trim(), StringComparison.Ordinal))
        {
            throw new PublishJobIdempotencyConflictException(job.IdempotencyKey);
        }
    }

    private async Task<(PublishJob Job, ValidationReportDto ValidationReport, PublishReadinessReportDto? PublishReadiness, string? Message, string? ReportPath)> RunPipelineAsync(
        PublishJob job,
        CreatePublishJobRequest request,
        CancellationToken cancellationToken,
        PublishJobLease? lease = null)
    {
        ValidationReportDto? validationReport = null;
        PublishReadinessReportDto? publishReadiness = null;
        string? reportPath = null;
        try
        {
            validationReport = await validationService.ValidateAsync(
                new ValidateSequenceRequest(request.ApplicationId, request.SequenceNumber),
                cancellationToken);

            if (!validationReport.IsValid)
            {
                var failureMessage = string.Join(" | ", validationReport.Issues.Select(x => $"{x.Code}: {x.Message}"));
                PublishPipelineLog.ValidationFailed(
                    logger,
                    job.Id,
                    validationReport.Issues.Count(x => string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase)));
                job.MarkFailed(failureMessage);
                await PersistTerminalStateAsync(job, lease);
                await TryWriteTerminalAuditAsync(
                    entityType: "PublishJob",
                    entityId: job.Id.ToString(),
                    action: "ValidationFailed",
                    details: $"Profile={validationReport.ValidationProfile}; {failureMessage}");
                return (job, validationReport, null, "Publish stopped because validation failed.", reportPath);
            }

            publishReadiness = await publishReadinessService.GetAsync(
                new ValidateSequenceRequest(request.ApplicationId, request.SequenceNumber),
                validationReport,
                cancellationToken);

            if (!publishReadiness.IsReady)
            {
                var readinessFailureMessage = string.Join(
                    " | ",
                    publishReadiness.Findings
                        .Where(x => string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                        .Select(x => $"{x.Code}: {x.Message}"));
                if (string.IsNullOrWhiteSpace(readinessFailureMessage))
                {
                    readinessFailureMessage = "Publish readiness check failed.";
                }

                PublishPipelineLog.ReadinessBlocked(logger, job.Id, publishReadiness.BlockingErrorCount);
                job.MarkFailed(readinessFailureMessage);
                await PersistTerminalStateAsync(job, lease);
                await TryWriteTerminalAuditAsync(
                    entityType: "PublishJob",
                    entityId: job.Id.ToString(),
                    action: "ReadinessBlocked",
                    details: $"Profile={publishReadiness.ValidationReport.ValidationProfile}; {readinessFailureMessage}");
                return (job, validationReport, publishReadiness, "Publish stopped because publish readiness check failed.", reportPath);
            }

            if (job.Status == PublishJobStatus.Pending)
            {
                job.MarkRunning();
                await repository.UpdateAsync(job, cancellationToken);
            }
            else if (job.Status != PublishJobStatus.Running || lease is null)
            {
                throw new InvalidOperationException($"Publish job {job.Id} cannot start from status {job.Status}.");
            }

            PublishPipelineLog.ExecutionStarted(logger, job.Id);
            await TryWriteAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Started",
                details: $"Publish execution started. Profile={validationReport.ValidationProfile}",
                cancellationToken,
                ignoreCancellation: true);

            var generated = await backboneService.GenerateAsync(
                new GenerateBackboneRequest(
                    request.ApplicationId,
                    request.SequenceNumber,
                    job.Id,
                    $"publish-report-{request.SequenceNumber}-{job.Id:N}.json",
                    $"{request.SequenceNumber}-{job.Id:N}.zip"),
                cancellationToken);
            reportPath = generated.ReportPath;

            job.MarkCompleted(generated.FilePath, generated.PackagePath);
            PublishPipelineLog.Completed(logger, job.Id, generated.FilePath, generated.PackagePath);
            await PersistTerminalStateAsync(job, lease);
            await TryWriteTerminalAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: "Completed",
                details: $"Profile={validationReport.ValidationProfile}; Output: {generated.FilePath}; Package: {generated.PackagePath}");

            return (job, validationReport, publishReadiness, "Publish completed successfully.", reportPath);
        }
        catch (Exception exception)
        {
            PublishPipelineLog.ExecutionFailed(logger, exception, job.Id);
            if (lease is not null)
            {
                // 持久 worker 由外层依据 attempt/lease 决定重试或终态；这里不能抢先
                // 清除租约，否则旧 worker 将绕过 fencing token 写入状态。
                throw;
            }

            if (job.Status is not PublishJobStatus.Completed and not PublishJobStatus.Failed)
            {
                var failureReason = exception is OperationCanceledException
                    ? "Publish execution was canceled or timed out."
                    : exception.Message;
                job.MarkFailed(failureReason);
            }

            await PersistTerminalStateAsync(job);
            await TryWriteTerminalAuditAsync(
                entityType: "PublishJob",
                entityId: job.Id.ToString(),
                action: job.Status == PublishJobStatus.Completed ? "Completed" : "Failed",
                details: validationReport is null
                    ? exception.Message
                    : $"Profile={validationReport.ValidationProfile}; {exception.Message}");

            validationReport ??= await validationService.ValidateAsync(
                new ValidateSequenceRequest(request.ApplicationId, request.SequenceNumber),
                cancellationToken);

            return (job, validationReport, publishReadiness, "Publish failed during execution.", reportPath);
        }
    }

    private async Task PersistTerminalStateAsync(PublishJob job, PublishJobLease? lease = null)
    {
        try
        {
            await PersistTerminalStateOnceAsync(job, lease);
        }
        catch (Exception exception)
        {
            PublishPipelineLog.TerminalPersistenceRetry(logger, exception, job.Id, job.Status.ToString());
            await PersistTerminalStateOnceAsync(job, lease);
        }
    }

    private async Task PersistTerminalStateOnceAsync(PublishJob job, PublishJobLease? lease)
    {
        using var cleanupCts = new CancellationTokenSource(TerminalCleanupTimeout);
        if (lease is null)
        {
            await repository.UpdateAsync(job, cleanupCts.Token);
            return;
        }

        if (await repository.UpdateLeasedAsync(
                job,
                lease.Token,
                lease.Owner,
                DateTime.UtcNow,
                cleanupCts.Token))
        {
            return;
        }

        // 首次保存可能在数据库提交成功后丢失响应。读取终态可区分这种歧义与真实失租。
        var persisted = await repository.GetAsync(job.Id, cleanupCts.Token);
        if (persisted is not null
            && persisted.Status == job.Status
            && string.Equals(persisted.OutputPath, job.OutputPath, StringComparison.Ordinal)
            && string.Equals(persisted.PackagePath, job.PackagePath, StringComparison.Ordinal)
            && string.Equals(persisted.FailureReason, job.FailureReason, StringComparison.Ordinal))
        {
            return;
        }

        throw new PublishJobLeaseLostException(job.Id);
    }

    private async Task TryWriteTerminalAuditAsync(
        string entityType,
        string entityId,
        string action,
        string? details)
    {
        using var cleanupCts = new CancellationTokenSource(TerminalCleanupTimeout);
        await TryWriteAuditAsync(
            entityType,
            entityId,
            action,
            details,
            cleanupCts.Token,
            ignoreCancellation: true);
    }

    // Best-effort 预检：在正常路径上给出友好的"已有活动作业"冲突提示。
    // 防重的事实来源是 IPublishJobRepository.AddAsync —— 关系型 provider 的活动作业
    // 部分唯一索引与 InMemory 仓储的活动作业守卫，两者都会在并发竞态下拒绝第二个活动作业。
    private async Task EnsureNoActivePublishAsync(CreatePublishJobRequest request, CancellationToken cancellationToken)
    {
        var pending = await repository.QueryHistoryAsync(
            new PublishJobHistoryQuery(request.ApplicationId, request.SequenceNumber, PublishJobStatus.Pending.ToString(), null, null, 1, 1),
            cancellationToken);

        if (pending.TotalCount > 0)
        {
            throw new PublishJobAlreadyInProgressException(
                $"A publish job is already pending for application {request.ApplicationId}, sequence {request.SequenceNumber}.");
        }

        var running = await repository.QueryHistoryAsync(
            new PublishJobHistoryQuery(request.ApplicationId, request.SequenceNumber, PublishJobStatus.Running.ToString(), null, null, 1, 1),
            cancellationToken);

        if (running.TotalCount > 0)
        {
            throw new PublishJobAlreadyInProgressException(
                $"A publish job is already running for application {request.ApplicationId}, sequence {request.SequenceNumber}.");
        }
    }

    private async Task TryWriteAuditAsync(
        string entityType,
        string entityId,
        string action,
        string? details,
        CancellationToken cancellationToken,
        bool ignoreCancellation = false)
    {
        try
        {
            await auditLogService.WriteSystemEventAsync(
                new CreateAuditLogRequest(entityType, entityId, action, details),
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (ignoreCancellation)
        {
            PublishPipelineLog.AuditWriteFailed(logger, exception, entityType, entityId, action);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 审计写入不阻断发布，但缺失必须留痕——对监管提交系统，静默丢审计是合规风险。
            PublishPipelineLog.AuditWriteFailed(logger, exception, entityType, entityId, action);
        }
    }

    public async Task<PublishJobDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        return job?.ToDto();
    }

    public async Task<IReadOnlyCollection<PublishJobDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await repository.ListAsync(cancellationToken);
        return jobs.Select(x => x.ToDto()).ToArray();
    }
}

internal static class PublishJobMapping
{
    public static PublishJobDto ToDto(this PublishJob job)
    {
        return new PublishJobDto(
            job.Id,
            job.ApplicationId,
            job.SequenceNumber,
            job.Status.ToString(),
            job.OutputPath,
            job.PackagePath,
            job.CreatedUtc,
            job.CompletedUtc,
            job.FailureReason,
            job.IdempotencyKey,
            job.AttemptCount,
            job.NextAttemptUtc);
    }
}
