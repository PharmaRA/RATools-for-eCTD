using RATools.Application.Abstractions.Persistence;
using RATools.Application.Documents;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.TestDoubles;

internal static class PackageTestServices
{
    public static EctdPackageModelBuilder Create(IApplicationRepository applications, IDocumentPlacementRepository placements,
        IDocumentRepository documents, IStandardsProfileProvider standards, IDocumentStorageBoundary boundary,
        ICtdNodeRepository? nodes = null, IWorkspaceRevisionStore? revisions = null, IApplicationImportStore? imports = null)
    {
        revisions ??= new InMemoryWorkspaceRevisionStore(applications);
        var memoryNodes = new InMemoryCtdNodeRepository(revisions, placements);
        return new(applications, placements, documents, standards, boundary,
            nodes ?? memoryNodes, revisions, imports ?? new InMemoryApplicationImportStore(applications, documents, placements, memoryNodes));
    }
}
