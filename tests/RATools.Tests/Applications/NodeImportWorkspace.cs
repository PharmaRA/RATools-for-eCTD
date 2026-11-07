using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Domain.Documents;
using RATools.Domain.Common;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Applications;

internal sealed class NodeImportWorkspace : IDisposable
{
    private readonly string _temporaryRoot = Path.GetFullPath(Path.Combine(".artifacts", "node-import-tests", "node-import-" + Guid.NewGuid().ToString("N")));
    public string Root { get; }
    public IApplicationRepository Applications { get; }
    public IDocumentRepository Documents { get; }
    public ImportFaultingPlacements Placements { get; }
    public ICtdNodeRepository Nodes { get; }
    public IWorkspaceRevisionStore Revisions { get; }
    public IApplicationImportStore Store { get; }
    public ApplicationImportBatch? Batch { get; private set; }
    public ApplicationImportService Service { get; }

    public NodeImportWorkspace(RAToolsDbContext? database = null, string? applicationNumber = null)
    {
        Root = applicationNumber is null ? _temporaryRoot : Path.Combine(_temporaryRoot,
            PortablePathSegment.NormalizeAndValidate(applicationNumber, nameof(applicationNumber)));
        Directory.CreateDirectory(Root);
        Applications = database is null ? new InMemoryApplicationRepository() : new EfCoreApplicationRepository(database);
        Documents = database is null ? new InMemoryDocumentRepository() : new EfCoreDocumentRepository(database);
        Placements = new(database is null ? new InMemoryDocumentPlacementRepository() : new EfCoreDocumentPlacementRepository(database));
        Revisions = database is null ? new InMemoryWorkspaceRevisionStore(Applications) : new EfCoreWorkspaceRevisionStore(database);
        Nodes = database is null ? new InMemoryCtdNodeRepository(Revisions, Placements)
            : new EfCoreCtdNodeRepository(database);
        Store = database is null ? new InMemoryApplicationImportStore(Applications, Documents, Placements, (InMemoryCtdNodeRepository)Nodes, new InMemoryRegionalMetadataStore())
            : new EfCoreApplicationImportStore(database, Applications, Documents, Placements, new EfCoreRegionalMetadataStore(database));
        Service = new(Applications, new CapturingStore(this), new AllowedPaths(), ImportAcceptanceFixture.MemorySnapshots());
    }

    public Task<ApplicationImportResultDto> ImportAsync(CancellationToken cancellationToken = default) =>
        Service.ImportAsync(new ImportApplicationRequest(Root, "us-fda-ectd-3.2.2", "Sponsor must not become a manufacturer"), cancellationToken);

    public void CopyPublisherFixture()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Publisher", "sequences");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public async Task WriteAsync(string number, params XElement[] sections)
    {
        var folder = Path.Combine(Root, number);
        Directory.CreateDirectory(folder);
        var xml = new XDocument(new XElement("ectd", new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3.org/1999/xlink"), sections));
        foreach (var leaf in xml.Descendants("leaf").Where(leaf => leaf.Attribute("operation")!.Value != "delete"))
        {
            var href = leaf.Attributes().Single(attribute => attribute.Name.LocalName == "href").Value;
            var path = Path.Combine(folder, Uri.UnescapeDataString(href));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "synthetic content " + leaf.Attribute("ID")!.Value);
        }
        xml.Save(Path.Combine(folder, "index.xml"));
    }

    public static XElement Leaf(string id, string href = "specification.pdf", string operation = "new", string? target = null) =>
        new("leaf", new XAttribute("ID", id), new XAttribute("operation", operation),
            operation == "delete" ? null : new XAttribute(XName.Get("href", "http://www.w3.org/1999/xlink"), href),
            target is null ? null : new XAttribute("modified-file", target), new XElement("title", id));

    public static XElement Substance(string? manufacturer, params XElement[] leaves) =>
        new("m3-2-s-drug-substance", new XAttribute("substance", "Drug A"),
            manufacturer is null ? null : new XAttribute("manufacturer", manufacturer),
            new XElement("m3-2-s-4-control-of-drug-substance", new XElement("m3-2-s-4-1-specification", leaves)));

    public static XElement Quality(params XElement[] substances) => new("m3-quality", new XElement("m3-2-body-of-data", substances));

    public static XElement Studies(params XElement[] extensions) => new("m4-nonclinical-study-reports",
        new XElement("m4-2-study-reports", new XElement("m4-2-3-toxicology", new XElement("m4-2-3-2-repeat-dose-toxicity", extensions))));

    public static XElement Extension(string id, string title, params XElement[] children) =>
        new("node-extension", new XAttribute("ID", id), new XElement("title", title), children);

    public void RetainMissingPayload(string sequence, string fileName)
    {
        var source = Path.GetFullPath(Path.Combine(Root,
            PortablePathSegment.NormalizeAndValidate(sequence, nameof(sequence)),
            PortablePathSegment.NormalizeAndValidate(fileName, nameof(fileName))));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!source.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Missing-payload fixture escaped its workspace.");
        EnsureNoLinks(source);
        var recycle = Path.Combine(Path.GetDirectoryName(_temporaryRoot)!, "ratools-node-import-recycle");
        EnsureNoLinks(recycle); Directory.CreateDirectory(recycle); EnsureNoLinks(recycle);
        var destination = Path.GetFullPath(Path.Combine(recycle, Guid.NewGuid().ToString("N") + "-" + fileName));
        if (!string.Equals(Path.GetDirectoryName(destination), recycle, comparison))
            throw new InvalidOperationException("Missing-payload recycle destination escaped.");
        var original = File.ReadAllBytes(source);
        File.Move(source, destination);
        Assert.False(File.Exists(source));
        Assert.Equal(original, File.ReadAllBytes(destination));
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(".artifacts", "node-import-tests"));
        var source = Path.GetFullPath(_temporaryRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(source), parent, comparison) ||
            !Path.GetFileName(source).StartsWith("node-import-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(source)[12..], "N", out _)) throw new InvalidOperationException("Unexpected import fixture ownership.");
        var pending = new Stack<string>(); pending.Push(source);
        while (pending.TryPop(out var current))
        {
            EnsureNoLinks(current);
            if (Directory.Exists(current))
                foreach (var child in Directory.EnumerateFileSystemEntries(current)) pending.Push(child);
        }
        var recycle = Path.Combine(parent, "ratools-node-import-recycle");
        EnsureNoLinks(recycle); Directory.CreateDirectory(recycle); EnsureNoLinks(recycle);
        var destination = Path.GetFullPath(Path.Combine(recycle, Path.GetFileName(source) + "-" + Guid.NewGuid().ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(destination), recycle, comparison)) throw new InvalidOperationException("Fixture recycle destination escaped.");
        Directory.Move(source, destination);
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Import fixture links require inspection before retention.");
    }

    private sealed class AllowedPaths : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];
        public string EnsureAllowed(string path) => Path.GetFullPath(path);
    }

    private sealed class CapturingStore(NodeImportWorkspace owner) : IApplicationImportStore
    {
        public Task SaveAsync(ApplicationImportBatch batch, CancellationToken cancellationToken = default)
        {
            owner.Batch = batch;
            return owner.Store.SaveAsync(batch, cancellationToken);
        }
        public Task<IReadOnlyList<ImportedBackbone>> GetBackbonesAsync(Guid id, string sequence, CancellationToken cancellationToken = default) =>
            owner.Store.GetBackbonesAsync(id, sequence, cancellationToken);
    }
}

internal sealed class ImportFaultingPlacements(IDocumentPlacementRepository inner) : IDocumentPlacementRepository
{
    public bool FailAfterAdd { get; set; }
    public CancellationTokenSource? CancelAfterAdd { get; set; }
    public async Task AddAsync(DocumentPlacement item, CancellationToken cancellationToken = default)
    {
        await inner.AddAsync(item, cancellationToken);
        CancelAfterAdd?.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        if (FailAfterAdd) throw new IOException("Injected failure after placement write");
    }
    public Task<bool> UpdateAsync(DocumentPlacement item, CancellationToken cancellationToken = default) => inner.UpdateAsync(item, cancellationToken);
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
    public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid id, CancellationToken cancellationToken = default) => inner.ListByApplicationAsync(id, cancellationToken);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid id, string sequence, CancellationToken cancellationToken = default) => inner.ListBySequenceAsync(id, sequence, cancellationToken);
}
