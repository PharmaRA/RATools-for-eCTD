namespace RATools.Application.Publishing;

public sealed class PublishJobIdempotencyConflictException : InvalidOperationException
{
    public PublishJobIdempotencyConflictException(string idempotencyKey)
        : base($"Idempotency key '{idempotencyKey}' is already associated with a different publish request.")
    {
    }
}
