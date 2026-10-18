namespace RATools.Application.Publishing.Requests;

public sealed record CreatePublishJobRequest(
    Guid ApplicationId,
    string SequenceNumber,
    string? IdempotencyKey = null,
    // Optional explicit reference to a completed independent validation run. The
    // service verifies that the run completed, belongs to this application and
    // sequence, and still has an archived report before recording it.
    Guid? PackageValidationRunId = null);
