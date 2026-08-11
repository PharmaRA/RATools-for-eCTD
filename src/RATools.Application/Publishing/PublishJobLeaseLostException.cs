namespace RATools.Application.Publishing;

public sealed class PublishJobLeaseLostException : InvalidOperationException
{
    public PublishJobLeaseLostException(Guid jobId)
        : base($"Publish job {jobId} lease is no longer owned by this worker.")
    {
    }
}
