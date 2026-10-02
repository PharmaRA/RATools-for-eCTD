using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Ctd;

namespace RATools.Application.Ctd;

public sealed class NodeFileMoveGuard(IApplicationRepository applications, NodeFileMoveJournal journal)
{
    public async Task EnsureReadyAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken);
        if (application is not null && journal.Read(application).Count > 0)
            throw new CtdNodeConstraintException("NodeMoveRecoveryRequired", "A pending file move requires recovery before this workspace can be edited or published.");
    }
}
