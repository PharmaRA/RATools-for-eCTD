using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Ctd;
using RATools.Application.Documents;
using RATools.Application.Validation;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Workspaces;

internal sealed class NodeMoveTestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "node-move-" + Guid.NewGuid().ToString("N"));
    public SubmissionApplication Application { get; }
    public IApplicationRepository Applications { get; }
    public IDocumentRepository Documents { get; }
    public FaultingPlacements Placements { get; }
    public ICtdNodeRepository Nodes { get; }
    public IWorkspaceRevisionStore Revisions { get; }
    public WorkspaceMutationCoordinator Mutations { get; }
    public CtdSequenceWorkspace Workspace { get; private set; } = null!;
    public SequenceNode Alpha => Workspace.Nodes.First(node => node.CtdSection == "m3.2.s.4.1");
    public SequenceNode Beta => Workspace.Nodes.Last(node => node.CtdSection == "m3.2.s.4.1");
    public CtdNodePathResolver Paths { get; } = new(new EctdWorkspacePathResolver());
    public DocumentStorageBoundary Boundary { get; }
    public NodeFileMoveJournal Journal { get; }
    public FaultingFiles Files { get; } = new();
    public CtdNodePlacementService Service { get; }

    public NodeMoveTestWorkspace(RAToolsDbContext? database = null)
    {
        Directory.CreateDirectory(Root);
        Application = new SubmissionApplication("move-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", Root, "us-fda-ectd-3.2.2");
        Application.CreateSequence("0000", "original", "Synthetic move tests");
        Applications = database is null ? new InMemoryApplicationRepository() : new EfCoreApplicationRepository(database);
        Documents = database is null ? new InMemoryDocumentRepository() : new EfCoreDocumentRepository(database);
        Placements = new FaultingPlacements(database is null ? new InMemoryDocumentPlacementRepository() : new EfCoreDocumentPlacementRepository(database));
        Revisions = database is null ? new InMemoryWorkspaceRevisionStore(Applications) : new EfCoreWorkspaceRevisionStore(database);
        Nodes = database is null ? new InMemoryCtdNodeRepository(Revisions, Placements) : new EfCoreCtdNodeRepository(database);
        Mutations = new(Revisions, database is null ? new InMemoryPersistenceTransaction() : new EfCorePersistenceTransaction(database));
        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [Root] }));
        Boundary = new(policy);
        Journal = new(policy);
        Service = new(Nodes, Placements, Documents, Applications, Boundary, Paths, Files, Mutations, Journal);
    }

    public async Task SeedAsync()
    {
        await Applications.AddAsync(Application);
        Workspace = CtdNodePathTests.Create(applicationId: Application.Id);
        await Nodes.SaveSequenceAsync(Workspace.Graph, "0000", Workspace.Nodes, 0);
    }

    public async Task<(SubmissionDocument Document, DocumentPlacement Placement)> AddAsync(string folder = "imported", SequenceNode? node = null)
    {
        var path = Path.Combine(Root, "0000", folder, "specification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = System.Text.Encoding.UTF8.GetBytes("Synthetic PDF payload " + folder);
        await File.WriteAllBytesAsync(path, content);
        var document = new SubmissionDocument("specification.pdf", "application/pdf", content.Length,
            Convert.ToHexString(SHA256.HashData(content)), Convert.ToHexString(MD5.HashData(content)), path);
        var placement = new DocumentPlacement(document.Id, Application.Id, "0000", "m3.2.s.4.1", DocumentPlacementOperation.New, "Specification", "leaf-" + Guid.NewGuid().ToString("N"));
        if (node is not null) placement.BindToNode(node, 3);
        await Documents.AddAsync(document);
        await Placements.AddAsync(placement);
        return (document, placement);
    }

    public string Destination(Guid node) => Path.Combine(Root, "0000", Paths.ResolveFile(Application.EctdTemplateKey, Workspace, node, "specification.pdf").Replace('/', Path.DirectorySeparatorChar));
    public Task<long?> RevisionAsync() => Revisions.GetRevisionAsync(Application.Id, "0000");
    public NodeFileMoveRecovery Recovery() => new(Applications, Documents, Placements, Revisions, Boundary, Files, Journal);

    public NodeFileMoveIntent Prepare(SubmissionDocument document, DocumentPlacement placement)
    {
        var intent = new NodeFileMoveIntent(placement.Id, document.Id, Application.Id, "0000", 1, placement.NodeInstanceId,
            placement.CtdSection, placement.SortOrder, Beta.NodeInstanceId, Beta.CtdSection, 7, document.StoragePath,
            Destination(Beta.NodeInstanceId), document.FileSize, document.Sha256);
        Journal.Prepare(Application, intent);
        return intent;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal sealed class FaultingFiles : IFileStorage
{
    private readonly LocalFileStorage _inner = new(Options.Create(new FileStorageOptions()));
    private int _renames;
    public int ThrowBeforeRename { get; set; }
    public int ThrowAfterRename { get; set; }
    public CancellationTokenSource? CancelAfterMove { get; set; }
    public Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default) => _inner.SaveAsync(request, cancellationToken);
    public Task<string> MoveAsync(string sourcePath, string destinationDirectoryPath, CancellationToken cancellationToken = default) => _inner.MoveAsync(sourcePath, destinationDirectoryPath, cancellationToken);
    public async Task<string> RenameAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        var call = ++_renames;
        if (call == ThrowBeforeRename) throw new IOException("Injected failure before rename");
        var result = await _inner.RenameAsync(sourcePath, targetPath, cancellationToken);
        if (call == 1 && CancelAfterMove is { } cancellation)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (call == ThrowAfterRename) throw new IOException("Injected failure after rename");
        return result;
    }
}

internal sealed class FaultingPlacements(IDocumentPlacementRepository inner) : IDocumentPlacementRepository
{
    public int FailuresRemaining { get; set; }
    public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => inner.AddAsync(placement, cancellationToken);
    public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
    {
        if (FailuresRemaining-- > 0) throw new InvalidOperationException("Injected placement write failure");
        return inner.UpdateAsync(placement, cancellationToken);
    }
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
    public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid id, CancellationToken cancellationToken = default) => inner.ListByApplicationAsync(id, cancellationToken);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid id, string sequence, CancellationToken cancellationToken = default) => inner.ListBySequenceAsync(id, sequence, cancellationToken);
}
