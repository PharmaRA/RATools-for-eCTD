using RATools.Domain.Publishing;
using Xunit;

namespace RATools.Application.Tests.Publishing;

public sealed class PublishJobStateMachineTests
{
    [Fact]
    public void MarkCompleted_Throws_WhenJobIsNotRunning()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");

        Assert.Throws<InvalidOperationException>(() => job.MarkCompleted("index.xml", "0000.zip"));
    }

    [Fact]
    public void MarkRunning_Throws_WhenJobIsAlreadyCompleted()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");
        job.MarkRunning();
        job.MarkCompleted("index.xml", "0000.zip");

        Assert.Throws<InvalidOperationException>(() => job.MarkRunning());
    }

    [Fact]
    public void MarkFailed_Throws_WhenJobIsAlreadyCompleted()
    {
        var job = new PublishJob(Guid.NewGuid(), "0000");
        job.MarkRunning();
        job.MarkCompleted("index.xml", "0000.zip");

        Assert.Throws<InvalidOperationException>(() => job.MarkFailed("boom"));
    }
}
