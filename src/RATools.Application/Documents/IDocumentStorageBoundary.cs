using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Application.Documents;

public interface IDocumentStorageBoundary
{
    string EnsureAllowedDocumentPath(SubmissionDocument document);

    string EnsureDocumentOwnedBySequence(
        SubmissionDocument document,
        SubmissionApplication application,
        string sequenceNumber);

    string EnsurePathOwnedBySequence(
        string path,
        SubmissionApplication application,
        string sequenceNumber);
}
