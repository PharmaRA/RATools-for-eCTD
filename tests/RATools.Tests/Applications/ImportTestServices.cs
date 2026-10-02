using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Applications;

internal static class ImportTestServices
{
    public static ApplicationImportService Create(IApplicationRepository applications, IDocumentRepository documents,
        IDocumentPlacementRepository placements, IWorkspacePathPolicy paths)
    {
        var nodes = new InMemoryCtdNodeRepository(new InMemoryWorkspaceRevisionStore(applications), placements);
        return new ApplicationImportService(applications,
            new InMemoryApplicationImportStore(applications, documents, placements, nodes), paths);
    }
}
