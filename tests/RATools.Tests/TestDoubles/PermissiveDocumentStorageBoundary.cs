using RATools.Application.Documents;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Tests.TestDoubles;

internal sealed class PermissiveDocumentStorageBoundary : IDocumentStorageBoundary
{
    public static PermissiveDocumentStorageBoundary Instance { get; } = new();

    public string EnsureAllowedDocumentPath(SubmissionDocument document) => document.StoragePath;

    public string EnsureDocumentOwnedBySequence(
        SubmissionDocument document,
        SubmissionApplication application,
        string sequenceNumber) => document.StoragePath;

    public string EnsurePathOwnedBySequence(
        string path,
        SubmissionApplication application,
        string sequenceNumber) => path;
}
