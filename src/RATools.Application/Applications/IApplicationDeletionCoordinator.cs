using RATools.Domain.Applications;

namespace RATools.Application.Applications;

public interface IApplicationDeletionCoordinator
{
    Task DeleteApplicationAsync(
        SubmissionApplication application,
        ApplicationDeleteMode deleteMode,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteSequenceAsync(
        SubmissionApplication application,
        string sequenceNumber,
        ApplicationDeleteMode deleteMode,
        CancellationToken cancellationToken = default);
}
