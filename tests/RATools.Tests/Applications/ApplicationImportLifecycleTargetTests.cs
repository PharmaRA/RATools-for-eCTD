using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Tests.Applications;

public sealed class ApplicationImportLifecycleTargetTests
{
    [Fact]
    public async Task ImportAsync_RestoresLifecycleTargetFromModifiedFile()
    {
        using var workspace = new TemporaryDirectory();
        await WriteSequenceAsync(workspace.Path, "0001", "m1-1", "m1/us/11-forms/original.txt", "new", null, "Original Leaf", "original content");
        await WriteSequenceAsync(workspace.Path, "0002", "m1-1", "m1/us/11-forms/replacement.txt", "replace", "m1/us/11-forms/original.txt", "Replacement Leaf", "replacement content");

        var placementRepository = new CapturingDocumentPlacementRepository();
        var service = new ApplicationImportService(
            new CapturingApplicationRepository(),
            new CapturingDocumentRepository(),
            placementRepository,
            new AllowAllWorkspacePathPolicy());

        var result = await service.ImportAsync(new ImportApplicationRequest(workspace.Path, "us-fda-ectd-3.2.2", "Sponsor"));

        var original = Assert.Single(placementRepository.Placements, x => x.SequenceNumber == "0001");
        var replacement = Assert.Single(placementRepository.Placements, x => x.SequenceNumber == "0002");
        Assert.DoesNotContain(result.Issues, x => x.Severity == "Error");
        Assert.Equal(original.Id, replacement.LifecycleTargetPlacementId);
    }

    [Fact]
    public async Task ImportAsync_WarnsAndKeepsNullTargetWhenLifecycleModifiedFileIsMissing()
    {
        using var workspace = new TemporaryDirectory();
        await WriteSequenceAsync(workspace.Path, "0001", "m1-1", "m1/us/11-forms/replacement.txt", "replace", null, "Replacement Leaf", "replacement content");

        var placementRepository = new CapturingDocumentPlacementRepository();
        var service = new ApplicationImportService(
            new CapturingApplicationRepository(),
            new CapturingDocumentRepository(),
            placementRepository,
            new AllowAllWorkspacePathPolicy());

        var result = await service.ImportAsync(new ImportApplicationRequest(workspace.Path, "us-fda-ectd-3.2.2", "Sponsor"));

        var placement = Assert.Single(placementRepository.Placements);
        Assert.Null(placement.LifecycleTargetPlacementId);
        var warning = Assert.Single(result.Issues, x => x.Code == "LIFECYCLE_TARGET_MISSING");
        Assert.Equal("Warning", warning.Severity);
        Assert.Equal("0001", warning.SequenceNumber);
        Assert.Contains("m1/us/11-forms/replacement.txt", warning.Message);
    }

    [Fact]
    public async Task ImportAsync_WarnsAndKeepsNullTargetWhenModifiedFileWasNotImported()
    {
        using var workspace = new TemporaryDirectory();
        await WriteSequenceAsync(workspace.Path, "0001", "m1-1", "m1/us/11-forms/replacement.txt", "replace", "m1/us/11-forms/missing.txt", "Replacement Leaf", "replacement content");

        var placementRepository = new CapturingDocumentPlacementRepository();
        var service = new ApplicationImportService(
            new CapturingApplicationRepository(),
            new CapturingDocumentRepository(),
            placementRepository,
            new AllowAllWorkspacePathPolicy());

        var result = await service.ImportAsync(new ImportApplicationRequest(workspace.Path, "us-fda-ectd-3.2.2", "Sponsor"));

        var placement = Assert.Single(placementRepository.Placements);
        Assert.Null(placement.LifecycleTargetPlacementId);
        var warning = Assert.Single(result.Issues, x => x.Code == "LIFECYCLE_TARGET_NOT_IMPORTED");
        Assert.Equal("Warning", warning.Severity);
        Assert.Equal("0001", warning.SequenceNumber);
        Assert.Contains("m1/us/11-forms/missing.txt", warning.Message);
    }

    [Fact]
    public async Task ImportAsync_DoesNotMatchModifiedFileWithDifferentCaseOrWhitespace()
    {
        using var workspace = new TemporaryDirectory();
        await WriteSequenceAsync(workspace.Path, "0001", "m1-1", "m1/us/11-forms/original.txt", "new", null, "Original Leaf", "original content");
        await WriteSequenceAsync(workspace.Path, "0002", "m1-1", "m1/us/11-forms/replacement.txt", "replace", " M1/us/11-forms/original.txt ", "Replacement Leaf", "replacement content");

        var placementRepository = new CapturingDocumentPlacementRepository();
        var service = new ApplicationImportService(
            new CapturingApplicationRepository(),
            new CapturingDocumentRepository(),
            placementRepository,
            new AllowAllWorkspacePathPolicy());

        var result = await service.ImportAsync(new ImportApplicationRequest(workspace.Path, "us-fda-ectd-3.2.2", "Sponsor"));

        var replacement = Assert.Single(placementRepository.Placements, x => x.SequenceNumber == "0002");
        Assert.Null(replacement.LifecycleTargetPlacementId);
        Assert.Contains(result.Issues, x => x.Code == "LIFECYCLE_TARGET_NOT_IMPORTED" && x.Message.Contains(" M1/us/11-forms/original.txt "));
    }

    [Fact]
    public async Task ImportAsync_NormalizesBackslashesAndLeadingDotSlashForModifiedFile()
    {
        using var workspace = new TemporaryDirectory();
        await WriteSequenceAsync(workspace.Path, "0001", "m1-1", "m1/us/11-forms/original.txt", "new", null, "Original Leaf", "original content");
        await WriteSequenceAsync(workspace.Path, "0002", "m1-1", "m1/us/11-forms/replacement.txt", "replace", ".\\m1\\us\\11-forms\\original.txt", "Replacement Leaf", "replacement content");

        var placementRepository = new CapturingDocumentPlacementRepository();
        var service = new ApplicationImportService(
            new CapturingApplicationRepository(),
            new CapturingDocumentRepository(),
            placementRepository,
            new AllowAllWorkspacePathPolicy());

        await service.ImportAsync(new ImportApplicationRequest(workspace.Path, "us-fda-ectd-3.2.2", "Sponsor"));

        var original = Assert.Single(placementRepository.Placements, x => x.SequenceNumber == "0001");
        var replacement = Assert.Single(placementRepository.Placements, x => x.SequenceNumber == "0002");
        Assert.Equal(original.Id, replacement.LifecycleTargetPlacementId);
    }

    private static async Task WriteSequenceAsync(
        string applicationRoot,
        string sequenceNumber,
        string sectionElement,
        string href,
        string operation,
        string? modifiedFile,
        string title,
        string fileContent)
    {
        var sequenceRoot = Path.Combine(applicationRoot, sequenceNumber);
        var filePath = Path.Combine(sequenceRoot, href.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, fileContent);
        var checksum = ComputeMd5(filePath);
        var modifiedFileAttribute = string.IsNullOrWhiteSpace(modifiedFile) ? string.Empty : $" modified-file=\"{modifiedFile}\"";
        var indexXml = $$"""
            <ectd xmlns:xlink="http://www.w3.org/1999/xlink">
              <{{sectionElement}}>
                <leaf xlink:href="{{href}}" operation="{{operation}}" checksum="{{checksum}}"{{modifiedFileAttribute}}><title>{{title}}</title></leaf>
              </{{sectionElement}}>
            </ectd>
            """;
        await File.WriteAllTextAsync(Path.Combine(sequenceRoot, "index.xml"), indexXml);
    }

    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    private sealed class AllowAllWorkspacePathPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];

        public string EnsureAllowed(string path) => Path.GetFullPath(path);
    }

    private sealed class CapturingApplicationRepository : IApplicationRepository
    {
        public List<SubmissionApplication> Applications { get; } = [];

        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default)
        {
            Applications.Add(application);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Applications.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Applications.SingleOrDefault(x => x.Id == id));

        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>(Applications);
    }

    private sealed class CapturingDocumentRepository : IDocumentRepository
    {
        public List<SubmissionDocument> Documents { get; } = [];

        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default)
        {
            Documents.Add(document);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Documents.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Documents.SingleOrDefault(x => x.Id == id));

        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionDocument>>(Documents);
    }

    private sealed class CapturingDocumentPlacementRepository : IDocumentPlacementRepository
    {
        public List<DocumentPlacement> Placements { get; } = [];

        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default)
        {
            Placements.Add(placement);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Placements.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Placements.SingleOrDefault(x => x.Id == id));

        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(Placements);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(Placements.Where(x => x.ApplicationId == applicationId).ToArray());

        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(Placements.Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber).ToArray());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-import-lifecycle-{Guid.NewGuid():N}");
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
