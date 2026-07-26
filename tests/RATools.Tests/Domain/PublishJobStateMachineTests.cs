using RATools.Domain.Publishing;

namespace RATools.Tests.Domain;

/// <summary>
/// PublishJob 是全系统唯一有状态机的实体，此前没有任何直接的域层测试。
/// 覆盖全部合法/非法转移矩阵：Pending→Running→Completed / *→Failed（除终态）。
/// </summary>
public sealed class PublishJobStateMachineTests
{
    [Fact]
    public void NewJob_StartsPending()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");

        Assert.Equal(PublishJobStatus.Pending, job.Status);
        Assert.Null(job.CompletedUtc);
        Assert.Null(job.FailureReason);
    }

    [Fact]
    public void Constructor_TrimsSequenceNumberAndRejectsBlank()
    {
        var job = new PublishJob(Guid.NewGuid(), " 0001 ");
        Assert.Equal("0001", job.SequenceNumber);

        Assert.Throws<ArgumentException>(() => new PublishJob(Guid.NewGuid(), "  "));
    }

    [Fact]
    public void MarkRunning_MovesPendingToRunning()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");

        job.MarkRunning();

        Assert.Equal(PublishJobStatus.Running, job.Status);
    }

    [Fact]
    public void MarkRunning_RejectsNonPendingStates()
    {
        var running = new PublishJob(Guid.NewGuid(), "0000");
        running.MarkRunning();
        Assert.Throws<InvalidOperationException>(running.MarkRunning);

        var completed = CreateCompleted();
        Assert.Throws<InvalidOperationException>(completed.MarkRunning);

        var failed = CreateFailed();
        Assert.Throws<InvalidOperationException>(failed.MarkRunning);
    }

    [Fact]
    public void MarkCompleted_MovesRunningToCompletedWithPaths()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");
        job.MarkRunning();

        job.MarkCompleted("C:/out/index.xml", "C:/out/package.zip");

        Assert.Equal(PublishJobStatus.Completed, job.Status);
        Assert.Equal("C:/out/index.xml", job.OutputPath);
        Assert.Equal("C:/out/package.zip", job.PackagePath);
        Assert.NotNull(job.CompletedUtc);
        Assert.Null(job.FailureReason);
    }

    [Fact]
    public void MarkCompleted_RejectsNonRunningStates()
    {
        var pending = new PublishJob(Guid.NewGuid(), "0000");
        Assert.Throws<InvalidOperationException>(() => pending.MarkCompleted("out", "pkg"));

        var completed = CreateCompleted();
        Assert.Throws<InvalidOperationException>(() => completed.MarkCompleted("out", "pkg"));

        var failed = CreateFailed();
        Assert.Throws<InvalidOperationException>(() => failed.MarkCompleted("out", "pkg"));
    }

    [Fact]
    public void MarkCompleted_RejectsBlankPaths()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");
        job.MarkRunning();

        Assert.Throws<ArgumentException>(() => job.MarkCompleted("", "pkg"));
        Assert.Throws<ArgumentException>(() => job.MarkCompleted("out", " "));
    }

    [Theory]
    [InlineData(PublishJobStatus.Pending)]
    [InlineData(PublishJobStatus.Running)]
    public void MarkFailed_AllowedFromActiveStates(PublishJobStatus fromStatus)
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");
        if (fromStatus == PublishJobStatus.Running)
        {
            job.MarkRunning();
        }

        job.MarkFailed("boom");

        Assert.Equal(PublishJobStatus.Failed, job.Status);
        Assert.Equal("boom", job.FailureReason);
        Assert.Null(job.PackagePath);
        Assert.NotNull(job.CompletedUtc);
    }

    [Fact]
    public void MarkFailed_RejectsTerminalStates()
    {
        var completed = CreateCompleted();
        Assert.Throws<InvalidOperationException>(() => completed.MarkFailed("late failure"));

        var failed = CreateFailed();
        Assert.Throws<InvalidOperationException>(() => failed.MarkFailed("double failure"));
    }

    [Fact]
    public void MarkFailed_RequiresReason()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");

        Assert.Throws<ArgumentException>(() => job.MarkFailed("  "));
    }

    [Fact]
    public void Rehydrate_RestoresPersistedState()
    {
        var id = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var createdUtc = DateTime.UtcNow.AddHours(-1);
        var completedUtc = DateTime.UtcNow;

        var job = PublishJob.Rehydrate(
            id, applicationId, "0002", PublishJobStatus.Failed,
            "C:/out", null, createdUtc, completedUtc, "restored failure");

        Assert.Equal(id, job.Id);
        Assert.Equal(applicationId, job.ApplicationId);
        Assert.Equal(PublishJobStatus.Failed, job.Status);
        Assert.Equal("restored failure", job.FailureReason);
        Assert.Equal(createdUtc, job.CreatedUtc);
        Assert.Equal(completedUtc, job.CompletedUtc);
    }

    private static PublishJob CreateCompleted()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");
        job.MarkRunning();
        job.MarkCompleted("C:/out/index.xml", "C:/out/package.zip");
        return job;
    }

    private static PublishJob CreateFailed()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");
        job.MarkFailed("initial failure");
        return job;
    }
}
