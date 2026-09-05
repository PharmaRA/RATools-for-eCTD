using System.ComponentModel;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Applications;

public sealed class ApplicationImportSecurityTests
{
    [Fact]
    public async Task ImportAsync_RejectsWorkingDirectoryOutsideConfiguredRootsBeforeReadingDirectory()
    {
        var service = new ApplicationImportService(
            new StubApplicationRepository(),
            new StubDocumentRepository(),
            new StubDocumentPlacementRepository(),
            new RejectingWorkspacePathPolicy());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(
            new ImportApplicationRequest(Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}"), "us-fda-ectd-3.2.2", "Sponsor")));

        Assert.Contains("outside the configured workspace roots", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_RejectsSequenceDirectoryReparsePointBeforeReadingIndex()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(outsideRoot.Path, "index.xml"), "<ectd />");
        var sequenceLinkPath = Path.Combine(allowedRoot.Path, "0001");
        if (!TryCreateDirectorySymlink(sequenceLinkPath, outsideRoot.Path))
        {
            return;
        }

        var service = CreateImportService(allowedRoot.Path);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(
            new ImportApplicationRequest(allowedRoot.Path, "us-fda-ectd-3.2.2", "Sponsor")));

        Assert.Contains("reparse point", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_RejectsIndexXmlReparsePointBeforeLoadingXml()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var sequencePath = Path.Combine(allowedRoot.Path, "0001");
        Directory.CreateDirectory(sequencePath);
        var outsideIndexPath = Path.Combine(outsideRoot.Path, "index.xml");
        await File.WriteAllTextAsync(outsideIndexPath, "<ectd />");
        var indexLinkPath = Path.Combine(sequencePath, "index.xml");
        if (!TryCreateFileSymlink(indexLinkPath, outsideIndexPath))
        {
            return;
        }

        var service = CreateImportService(allowedRoot.Path);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(
            new ImportApplicationRequest(allowedRoot.Path, "us-fda-ectd-3.2.2", "Sponsor")));

        Assert.Contains("reparse point", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_RejectsLeafParentReparsePointBeforeReadingLeafFile()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var sequencePath = Path.Combine(allowedRoot.Path, "0001");
        Directory.CreateDirectory(sequencePath);
        await File.WriteAllTextAsync(Path.Combine(sequencePath, "index.xml"), """
            <ectd xmlns:xlink="http://www.w3.org/1999/xlink">
              <m1-1>
                <leaf xlink:href="docs/leaf.pdf" operation="new"><title>Leaf</title></leaf>
              </m1-1>
            </ectd>
            """);
        await File.WriteAllTextAsync(Path.Combine(outsideRoot.Path, "leaf.pdf"), "outside");
        var docsLinkPath = Path.Combine(sequencePath, "docs");
        if (!TryCreateDirectorySymlink(docsLinkPath, outsideRoot.Path))
        {
            return;
        }

        var service = CreateImportService(allowedRoot.Path);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(
            new ImportApplicationRequest(allowedRoot.Path, "us-fda-ectd-3.2.2", "Sponsor")));

        Assert.Contains("reparse point", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_RejectsRegionalBackboneReparsePointBeforeLoadingXml()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var sequencePath = Path.Combine(allowedRoot.Path, "0001");
        var regionalDirectory = Path.Combine(sequencePath, "m1", "us");
        Directory.CreateDirectory(regionalDirectory);
        await File.WriteAllTextAsync(Path.Combine(sequencePath, "index.xml"), "<ectd />");
        var outsideXml = Path.Combine(outsideRoot.Path, "regional.xml");
        await File.WriteAllTextAsync(outsideXml, "<must-not-be-read");
        if (!TryCreateFileSymlink(Path.Combine(regionalDirectory, "us-regional.xml"), outsideXml))
        {
            return;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateImportService(allowedRoot.Path).ImportAsync(
            new ImportApplicationRequest(allowedRoot.Path, "us-fda-ectd-3.2.2", "Sponsor")));

        Assert.Contains("reparse point", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_RejectsRegionalLeafParentReparsePointBeforeReadingDocument()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var sequencePath = Path.Combine(allowedRoot.Path, "0001");
        var regionalDirectory = Path.Combine(sequencePath, "m1", "us");
        Directory.CreateDirectory(regionalDirectory);
        await File.WriteAllTextAsync(Path.Combine(sequencePath, "index.xml"), "<ectd />");
        await File.WriteAllTextAsync(Path.Combine(regionalDirectory, "us-regional.xml"), """
            <fda-regional xmlns:xlink="http://www.w3.org/1999/xlink">
              <m1-2-cover-letters><leaf xlink:href="docs/leaf.txt" operation="new"><title>Leaf</title></leaf></m1-2-cover-letters>
            </fda-regional>
            """);
        await File.WriteAllTextAsync(Path.Combine(outsideRoot.Path, "leaf.txt"), "must not be read");
        if (!TryCreateDirectorySymlink(Path.Combine(regionalDirectory, "docs"), outsideRoot.Path))
        {
            return;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateImportService(allowedRoot.Path).ImportAsync(
            new ImportApplicationRequest(allowedRoot.Path, "us-fda-ectd-3.2.2", "Sponsor")));

        Assert.Contains("reparse point", exception.Message);
    }

    private static ApplicationImportService CreateImportService(string allowedRoot)
    {
        return new ApplicationImportService(
            new StubApplicationRepository(),
            new StubDocumentRepository(),
            new StubDocumentPlacementRepository(),
            new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions
            {
                AllowedWorkspaceRoots = [allowedRoot]
            })));
    }

    private static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException or Win32Exception)
        {
            return false;
        }
    }

    private static bool TryCreateFileSymlink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException or Win32Exception)
        {
            return false;
        }
    }

    private sealed class RejectingWorkspacePathPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path)
            => throw new InvalidOperationException($"Path '{path}' is outside the configured workspace roots.");
    }

    private sealed class StubApplicationRepository : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<SubmissionApplication?>(null);
        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([]);
    }

    private sealed class StubDocumentRepository : IDocumentRepository
    {
        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<SubmissionDocument?>(null);
        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<SubmissionDocument>>([]);
    }

    private sealed class StubDocumentPlacementRepository : IDocumentPlacementRepository
    {
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DocumentPlacement?>(null);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>([]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-import-security-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
