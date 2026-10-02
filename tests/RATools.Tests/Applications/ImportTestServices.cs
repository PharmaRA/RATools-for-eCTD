using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Applications;

internal static class ImportTestServices
{
    public static ApplicationImportService Create(IApplicationRepository applications, IDocumentRepository documents,
        IDocumentPlacementRepository placements, IWorkspacePathPolicy paths)
        => Create(applications, documents, placements, paths, out _, out _);

    public static ApplicationImportService Create(IApplicationRepository applications, IDocumentRepository documents,
        IDocumentPlacementRepository placements, IWorkspacePathPolicy paths, out ICtdNodeRepository nodes, out IWorkspaceRevisionStore revisions)
    {
        revisions = new InMemoryWorkspaceRevisionStore(applications);
        var memoryNodes = new InMemoryCtdNodeRepository(revisions, placements);
        nodes = memoryNodes;
        return new ApplicationImportService(applications,
            new InMemoryApplicationImportStore(applications, documents, placements, memoryNodes), paths);
    }
}
