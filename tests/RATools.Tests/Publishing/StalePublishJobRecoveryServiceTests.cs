using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

public sealed class StalePublishJobRecoveryServiceTests
{
    private static (StalePublishJobRecoveryService Service, InMemoryPublishJobRepository Repository, InMemoryAuditLogRepository AuditRepository) CreateService(
        string persistenceProvider = "PostgreSql")
    {
        var repository = new InMemoryPublishJobRepository();
        var auditRepository = new InMemoryAuditLogRepository();

        var services = new ServiceCollection();
        services.AddSingleton<IPublishJobRepository>(repository);
        services.AddSingleton<IAuditLogRepository>(auditRepository);
        services.AddSingleton<IAuditLogService, AuditLogService>();
        var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = persistenceProvider,
            })
            .Build();

        var service = new StalePublishJobRecoveryService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<StalePublishJobRecoveryService>.Instance);

        return (service, repository, auditRepository);
    }

    [Fact]
    public async Task StartAsync_MarksStalePendingAndRunningJobsAsFailed()
    {
        var (service, repository, _) = CreateService();
        var pendingJob = new PublishJob(Guid.NewGuid(), "0000");
        var runningJob = new PublishJob(Guid.NewGuid(), "0001");
        runningJob.MarkRunning();
        await repository.AddAsync(pendingJob);
        await repository.AddAsync(runningJob);

        await service.StartAsync(CancellationToken.None);

        var recoveredPending = await repository.GetAsync(pendingJob.Id);
        var recoveredRunning = await repository.GetAsync(runningJob.Id);
        Assert.Equal(PublishJobStatus.Failed, recoveredPending!.Status);
        Assert.Equal(PublishJobStatus.Failed, recoveredRunning!.Status);
        Assert.Contains("Recovered at startup", recoveredPending.FailureReason);
        Assert.Contains("Recovered at startup", recoveredRunning.FailureReason);
    }

    [Fact]
    public async Task StartAsync_WritesRecoveryAuditEntries()
    {
        var (service, repository, auditRepository) = CreateService();
        var staleJob = new PublishJob(Guid.NewGuid(), "0000");
        await repository.AddAsync(staleJob);

        await service.StartAsync(CancellationToken.None);

        var entries = await auditRepository.ListAsync();
        var recoveryEntry = Assert.Single(entries, x => x.Action == "RecoveredAtStartup");
        Assert.Equal("PublishJob", recoveryEntry.EntityType);
        Assert.Equal(staleJob.Id.ToString(), recoveryEntry.EntityId);
    }

    [Fact]
    public async Task StartAsync_LeavesCompletedAndFailedJobsUntouched()
    {
        var (service, repository, auditRepository) = CreateService();
        var completedJob = new PublishJob(Guid.NewGuid(), "0000");
        completedJob.MarkRunning();
        completedJob.MarkCompleted("C:/out/index.xml", "C:/out/package.zip");
        var failedJob = new PublishJob(Guid.NewGuid(), "0001");
        failedJob.MarkFailed("Original failure.");
        await repository.AddAsync(completedJob);
        await repository.AddAsync(failedJob);

        await service.StartAsync(CancellationToken.None);

        var untouchedCompleted = await repository.GetAsync(completedJob.Id);
        var untouchedFailed = await repository.GetAsync(failedJob.Id);
        Assert.Equal(PublishJobStatus.Completed, untouchedCompleted!.Status);
        Assert.Equal("Original failure.", untouchedFailed!.FailureReason);
        Assert.Empty(await auditRepository.ListAsync());
    }

    [Fact]
    public async Task StartAsync_UnblocksSequenceForNewPublishJobs()
    {
        // 核心回归场景：幽灵 Pending 作业占用活动作业唯一约束，回收后同一
        // application/sequence 必须能再次创建新作业。
        var (service, repository, _) = CreateService();
        var applicationId = Guid.NewGuid();
        var ghostJob = new PublishJob(applicationId, "0000");
        await repository.AddAsync(ghostJob);

        await Assert.ThrowsAsync<PublishJobAlreadyInProgressException>(
            () => repository.AddAsync(new PublishJob(applicationId, "0000")));

        await service.StartAsync(CancellationToken.None);

        var newJob = new PublishJob(applicationId, "0000");
        await repository.AddAsync(newJob);
        var stored = await repository.GetAsync(newJob.Id);
        Assert.Equal(PublishJobStatus.Pending, stored!.Status);
    }

    [Fact]
    public async Task StartAsync_IsNoOpWhenNoActiveJobsExist()
    {
        var (service, _, auditRepository) = CreateService();

        await service.StartAsync(CancellationToken.None);

        Assert.Empty(await auditRepository.ListAsync());
    }

    [Fact]
    public async Task StartAsync_SkipsRecoveryForInMemoryProvider()
    {
        // InMemory 存储随进程消亡，重启后不存在遗留行；测试宿主也没有可连接的数据库。
        var (service, repository, auditRepository) = CreateService(persistenceProvider: "InMemory");
        var activeJob = new PublishJob(Guid.NewGuid(), "0000");
        await repository.AddAsync(activeJob);

        await service.StartAsync(CancellationToken.None);

        var untouched = await repository.GetAsync(activeJob.Id);
        Assert.Equal(PublishJobStatus.Pending, untouched!.Status);
        Assert.Empty(await auditRepository.ListAsync());
    }
}
