using System.Security.Cryptography;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Ctd;
using RATools.Application.DocumentProcessing;
using RATools.Application.PackageValidation;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfRenamePreviewTests
{
    [Fact]
    public async Task PreviewCapturesActualBytesAndRequiresTheReferencingDocumentToBeConfirmed()
    {
        var test = await SetupAsync();
        var preview = await test.Service.PreviewAsync(test.Application.Id, "0001", 0,
            [new(test.Target.Id, "renamed.pdf")], [test.Target.Id]);
        Assert.False(preview.Plan.CanExecute);
        Assert.Contains(preview.Plan.Impacts, impact => impact.SourcePlacementId == test.Source.Id && impact.TargetPlacementId == test.Target.Id
            && impact.NewReference == "renamed.pdf");
        Assert.Contains(preview.Plan.Issues, issue => issue.Code == "SOURCE_NOT_CONFIRMED" && issue.PlacementId == test.Source.Id);
        foreach (var source in preview.Sources)
        {
            var version = (await test.Versions.GetVersionAsync(source.VersionId))!;
            Assert.Equal(source.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(version.StoragePath))).ToLowerInvariant());
            Assert.NotEqual((await test.Documents.GetAsync(source.DocumentId))!.StoragePath, version.StoragePath);
        }
        var approved = await test.Service.PreviewAsync(test.Application.Id, "0001", 0,
            [new(test.Target.Id, "renamed.pdf")], [test.Target.Id, test.Source.Id]);
        Assert.True(approved.Plan.CanExecute);
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Equal("old.pdf", (await test.Documents.GetAsync(test.Target.DocumentId))!.FileName);
    }

    [Fact]
    public async Task PreviewRefusesStaleRevisionAndForeignPlacementsBeforeCapturingFiles()
    {
        var test = await SetupAsync();
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => test.Service.PreviewAsync(test.Application.Id, "0001", 1,
            [new(test.Target.Id, "renamed.pdf")], []));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service.PreviewAsync(test.Application.Id, "0001", 0,
            [new(Guid.NewGuid(), "renamed.pdf")], []));
        Assert.False(Directory.Exists(Path.Combine(test.Application.WorkingDirectoryPath, "_document-versions")));
    }

    private static async Task<Context> SetupAsync()
    {
        var directory = Path.GetFullPath(".artifacts/p507-preview-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "0001", "m5"));
        var application = new SubmissionApplication("rename-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", directory, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "Rename preview");
        var applications = new InMemoryApplicationRepository(); await applications.AddAsync(application);
        var documents = new InMemoryDocumentRepository();
        var placements = new InMemoryDocumentPlacementRepository();
        var bytes = await File.ReadAllBytesAsync("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var created = new List<DocumentPlacement>();
        foreach (var name in new[] { "source.pdf", "old.pdf" })
        {
            var path = Path.Combine(directory, "0001", "m5", name); await File.WriteAllBytesAsync(path, bytes);
            var document = new SubmissionDocument(name, "application/pdf", bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path);
            await documents.AddAsync(document);
            var placement = new DocumentPlacement(document.Id, application.Id, "0001", "m5.3.1", DocumentPlacementOperation.New, name);
            await placements.AddAsync(placement); created.Add(placement);
        }
        var revisions = new InMemoryWorkspaceRevisionStore(applications);
        var versions = new InMemoryDocumentProcessingStore();
        var options = new PdfProcessingExecutionOptions();
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe") });
        var service = new PdfRenamePreviewService(applications, documents, placements, null!, null!,
            new WorkspaceMutationCoordinator(revisions, new InMemoryPersistenceTransaction()),
            new ProcessingFileStore(new Paths(directory), options), versions, engine, new PdfPigPackageInspector(), options);
        return new(application, created[0], created[1], service, documents, versions, revisions);
    }
    private sealed record Context(SubmissionApplication Application, DocumentPlacement Source, DocumentPlacement Target,
        PdfRenamePreviewService Service, InMemoryDocumentRepository Documents, InMemoryDocumentProcessingStore Versions,
        InMemoryWorkspaceRevisionStore Revisions);
    private sealed class Paths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        {
            var full = Path.GetFullPath(path);
            if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Outside test workspace.");
            return full;
        }
    }
}
