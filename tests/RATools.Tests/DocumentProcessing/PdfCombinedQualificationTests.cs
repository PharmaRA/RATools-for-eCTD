using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.Assembly;
using RATools.Application.Ctd;
using RATools.Application.DocumentProcessing;
using RATools.Application.Documents;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.DocumentProcessing;

// These are service-boundary qualification tests with real PDFs, qpdf, PyMuPDF,
// backbone writers and P2 inspectors. In-memory repositories and retained test
// capture adapters do not establish PostgreSQL, browser, Linux or formal readiness.
public sealed class PdfCombinedQualificationTests
{
    private static readonly string[] ExpectedValues1 = new[] { "临床总览", "结果" };

    [Fact]
    public async Task AssembledProcessedRenamedBytesReachPublicationFrozenHistoryAndLaterReplacement()
    {
        using var test = await PdfCombinedQualificationRig.CreateAsync();
        var mounted = await test.AssembleAsync("0000", [test.Source, test.Target]);
        var source = mounted[0]; var target = mounted[1];
        var originalSource = await File.ReadAllBytesAsync(test.Source.StoragePath);
        var originalTarget = await File.ReadAllBytesAsync(test.Target.StoragePath);
        var assembled = (await test.Documents.GetAsync(source.DocumentId))!;
        var assembledBytes = await File.ReadAllBytesAsync(assembled.StoragePath);
        Assert.Equal(originalSource, assembledBytes);
        Assert.NotEqual(test.Source.Id, source.DocumentId);

        var navigation = await test.ProcessAsync("0000", source.Id,
            new("UseOutlines", [new("临床总览", 1, null, 0), new("结果", 2, null, 1)], null));
        var afterNavigation = await test.Processing.InspectAsync(test.App.Id, "0000", source.Id, await test.RevisionAsync("0000"));
        Assert.Equal("UseOutlines", afterNavigation.Navigation.PageMode);
        Assert.Equal(ExpectedValues1, afterNavigation.Navigation.Bookmarks.Select(bookmark => bookmark.Title));
        Assert.Equal(4, afterNavigation.Navigation.PageCount);
        Assert.Equal(source.LeafId, (await test.Placements.GetAsync(source.Id))!.LeafId);

        var content = await test.ProcessAsync("0000", source.Id, new(null, null, null, Content: new(null, true, "1.7")));
        var receipt = await test.Processing.GetContentReceiptAsync(test.App.Id, "0000", content.Id);
        Assert.Equal(navigation.ResultSha256, receipt.Receipt.SourceSha256);
        Assert.Equal(content.ResultSha256, receipt.Receipt.OutputSha256);
        Assert.Equal("1.7", receipt.Receipt.OutputVersion);
        Assert.NotEmpty(receipt.Receipt.Fonts!.Embedded);
        Assert.Equal("NotEvaluated", receipt.Receipt.RegulatoryConformance);

        var revision = await test.RevisionAsync("0000");
        var unconfirmed = await test.Rename.CreatePreviewAsync(test.App.Id, "0000",
            new(revision, [new(target.Id, "renamed-target.pdf")], [target.Id]));
        Assert.False(unconfirmed.Plan.CanExecute);
        Assert.Contains(unconfirmed.Plan.Issues, issue => issue.Code == "SOURCE_NOT_CONFIRMED" && issue.PlacementId == source.Id);
        var rename = await test.Rename.CreatePreviewAsync(test.App.Id, "0000",
            new(revision, [new(target.Id, "renamed-target.pdf")], [source.Id, target.Id]));
        Assert.True(rename.Plan.CanExecute, JsonSerializer.Serialize(rename.Plan));
        Assert.Contains(rename.Sources, item => item.PlacementId == source.Id);
        var queued = await test.Rename.EnqueueAsync(test.App.Id, "0000", rename.Id, rename.Plan.PlanDigest, "joint-rename");
        await test.Rename.ExecuteAsync((await test.Renames.ClaimAsync("joint", DateTime.UtcNow, TimeSpan.FromMinutes(3)))!);
        var renamed = await test.Rename.GetJobAsync(test.App.Id, "0000", queued.Job.State.Id);
        Assert.Equal(PdfProcessingJobStatus.Completed, renamed.Status);
        var adopted = await test.Rename.AdoptAsync(test.App.Id, "0000", renamed.Id, revision);
        Assert.Equal(revision + 1, adopted.WorkspaceRevision);
        var current = (await test.Documents.GetAsync((await test.Placements.GetAsync(source.Id))!.DocumentId))!;
        var finalSource = await File.ReadAllBytesAsync(current.StoragePath);
        Assert.NotEqual(PdfCombinedQualificationRig.Sha(originalSource), PdfCombinedQualificationRig.Sha(finalSource));
        var independent = await PdfCombinedQualificationRig.ReadPdfAsync(current.StoragePath);
        Assert.Equal(4, independent.GetProperty("pages").GetInt32());
        Assert.Equal("临床总览", independent.GetProperty("bookmarks")[0][1].GetString());
        Assert.Contains("Clinical report page 1", independent.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("renamed-target.pdf", independent.GetProperty("remoteFiles").EnumerateArray().Select(item => item.GetString()));
        Assert.DoesNotContain("target.pdf", independent.GetProperty("remoteFiles").EnumerateArray().Select(item => item.GetString()));

        var output = await test.PublishAsync("0000");
        var first = await test.ValidateAsync("0000", output);
        Assert.Empty(Assert.Single(first.Report.Coverage, check => check.RuleId == "PDF-LINKS").Findings);
        var remote = Assert.Single(first.Inspection.Pdf.Links, link => link.Link.Target.FileSpecification == "renamed-target.pdf");
        Assert.Equal(CheckStatus.Pass, remote.Status);
        Assert.EndsWith("/renamed-target.pdf", remote.TargetLogicalPath);
        Assert.Equal(2, remote.Link.Target.PageNumber);
        var firstLeaf = PdfCombinedQualificationRig.ReadIndex(output.FilePath).Descendants("leaf").Single(leaf => leaf.Attribute("ID")?.Value == source.LeafId);
        var firstHref = firstLeaf.Attributes().Single(attribute => attribute.Name.LocalName == "href").Value;
        Assert.Equal(PdfCombinedQualificationRig.Md5(finalSource), firstLeaf.Attribute("checksum")!.Value);
        Assert.Equal(finalSource, await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(output.FilePath)!, firstHref)));
        await PdfCombinedQualificationRig.AssertIndexChecksumAsync(output.FilePath);

        var snapshot = await test.FreezeAsync("0000", first.Run);
        Assert.Equal(PackageValidationMode.Development, snapshot.Sequence.ValidationMode);
        Assert.False(snapshot.Sequence.IsReadyForFinalization);
        var frozenPath = "payload/0000/" + firstHref;
        await using var frozen = await test.Finalization.GetReleaseFileAsync(snapshot.Sequence.Id, frozenPath);
        using var frozenBuffer = new MemoryStream();
        await frozen.Content.CopyToAsync(frozenBuffer);
        var frozenBytes = frozenBuffer.ToArray();
        Assert.Equal(finalSource, frozenBytes);
        var manifest = await test.Finalization.GetManifestAsync(snapshot.Sequence.Id);
        Assert.Equal(first.Run.ReportSha256, manifest.Validation.ReportSha256);
        Assert.Equal(first.Run.Binding.InputManifestDigest, manifest.ValidatedInputManifestDigest);
        Assert.Equal(snapshot.Sequence.ManifestDigest, manifest.Digest);
        foreach (var member in manifest.Files.Where(file => file.Kind == ReleaseFileKind.Payload))
        {
            const string prefix = "payload/0000/";
            Assert.StartsWith(prefix, member.RelativePath);
            await using var capturedFile = await test.Finalization.GetReleaseFileAsync(snapshot.Sequence.Id, member.RelativePath);
            var delivered = Path.Combine(Path.GetDirectoryName(output.FilePath)!, member.RelativePath[prefix.Length..]);
            using var capturedBytes = new MemoryStream();
            await capturedFile.Content.CopyToAsync(capturedBytes);
            Assert.Equal(await File.ReadAllBytesAsync(delivered), capturedBytes.ToArray());
        }

        // A frozen workspace must refuse adoption; the next regulatory sequence has
        // a new leaf and an explicit address into this exact earlier leaf.
        var frozenRevision = await test.RevisionAsync("0000");
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => test.Processing.InspectAsync(
            test.App.Id, "0000", source.Id, frozenRevision));
        await test.AddSequenceAsync("0001");
        var currentTarget = (await test.Documents.GetAsync((await test.Placements.GetAsync(target.Id))!.DocumentId))!;
        var retainedFrozenPath = Path.Combine(test.Root, "frozen-next-sequence.pdf");
        await File.WriteAllBytesAsync(retainedFrozenPath, frozenBytes);
        var nextSource = await test.UploadAsync("0001", current.FileName, retainedFrozenPath);
        var nextTarget = await test.UploadAsync("0001", currentTarget.FileName, currentTarget.StoragePath);
        Assert.Equal(frozen.Sha256, nextSource.Sha256);
        var next = (await test.AssembleAsync("0001", [nextSource, nextTarget], "replace", [source.Id.ToString("D"), target.Id.ToString("D")]))[0];
        Assert.NotEqual(source.LeafId, next.LeafId);
        Assert.Equal(source.Id, next.LifecycleTargetPlacementId);
        var later = await test.ProcessAsync("0001", next.Id, new("UseThumbs", null, null));
        var laterDocument = (await test.Documents.GetAsync((await test.Placements.GetAsync(next.Id))!.DocumentId))!;
        var laterBytes = await File.ReadAllBytesAsync(laterDocument.StoragePath);
        Assert.Equal(later.ResultSha256, PdfCombinedQualificationRig.Sha(laterBytes));
        Assert.NotEqual(PdfCombinedQualificationRig.Sha(frozenBytes), PdfCombinedQualificationRig.Sha(laterBytes));
        var laterOutput = await test.PublishAsync("0001");
        var laterLeaf = PdfCombinedQualificationRig.ReadIndex(laterOutput.FilePath).Descendants("leaf").Single(leaf => leaf.Attribute("ID")?.Value == next.LeafId);
        Assert.Equal("replace", laterLeaf.Attribute("operation")!.Value);
        Assert.Equal("../0000/index.xml#" + source.LeafId, laterLeaf.Attribute("modified-file")!.Value);
        Assert.Equal(PdfCombinedQualificationRig.Md5(laterBytes), laterLeaf.Attribute("checksum")!.Value);
        Assert.NotEqual(firstLeaf.Attribute("checksum")!.Value, laterLeaf.Attribute("checksum")!.Value);
        await PdfCombinedQualificationRig.AssertIndexChecksumAsync(laterOutput.FilePath);
        var second = await test.ValidateAsync("0001", laterOutput, first.Input.Id);
        var replacement = Assert.Single(second.Inspection.Lifecycle.Events, item => item.Source.Address?.LeafId == next.LeafId);
        Assert.Equal(source.LeafId, replacement.Target!.LeafId);
        Assert.Equal("0000", replacement.Target.SequenceNumber);
        Assert.Equal(CheckStatus.Pass, replacement.Status);
        await using var reopened = await test.Finalization.GetReleaseFileAsync(snapshot.Sequence.Id, frozenPath);
        using var reopenedBytes = new MemoryStream();
        await reopened.Content.CopyToAsync(reopenedBytes);
        Assert.Equal(frozenBytes, reopenedBytes.ToArray());
        Assert.Equal(originalSource, await File.ReadAllBytesAsync(test.Source.StoragePath));
        Assert.Equal(originalTarget, await File.ReadAllBytesAsync(test.Target.StoragePath));
        Assert.Equal(assembledBytes, await File.ReadAllBytesAsync(assembled.StoragePath));
        Assert.DoesNotContain(first.Report.Coverage.SelectMany(check => check.Findings), finding => finding.CheckStatus == CheckStatus.Fail);
        Assert.DoesNotContain(second.Report.Coverage.SelectMany(check => check.Findings), finding => finding.CheckStatus == CheckStatus.Fail);
        await test.EvidenceAsync(new { scenario = "assembly-navigation-content-rename-publish-freeze-replace", firstRun = first.Run.Id,
            secondRun = second.Run.Id, snapshot = snapshot.Sequence.Id, manifest = manifest.Digest, frozenSha256 = frozen.Sha256,
            laterSha256 = later.ResultSha256, firstLeaf = source.LeafId, laterLeaf = next.LeafId, output.FilePath,
            laterIndex = laterOutput.FilePath, receipt.Resources, storageProvider = "retained-test-adapter", formalQualification = false });
    }

    [Fact]
    public async Task LargePdfProcessingBoundsResourcesAllowsPublicationAndRecoversAfterActualCancellation()
    {
        using var test = await PdfCombinedQualificationRig.CreateAsync(160, large: true);
        Assert.True(test.Source.FileSize > 30 * 1024 * 1024);
        var placements = await test.AssembleAsync("0000", [test.Source, test.Target]);
        var revision = await test.RevisionAsync("0000");
        var inspected = await test.Processing.InspectAsync(test.App.Id, "0000", placements[0].Id, revision);
        var source = (await test.Versions.GetVersionAsync(inspected.SourceVersionId))!;
        var inspection = await test.Content.InspectAsync(source);
        Assert.Empty(inspection.BlockReasons); Assert.Equal(160, inspection.PageCount);
        var parameters = new PdfContentParameters(null, false, "1.7");
        var timer = Stopwatch.StartNew();
        var outputPath = Path.Combine(test.Root, "large-processed.pdf");
        using var processingCancellation = new CancellationTokenSource();
        var processing = test.Content.ExecuteAsync(source, outputPath, parameters, inspection, TimeSpan.FromMinutes(5), 128 * 1024 * 1024, processingCancellation.Token);
        GeneratedBackboneDto output;
        long publicationMilliseconds;
        PdfContentEvidence processed;
        using var previewCancellation = new CancellationTokenSource();
        Task<PdfContentInspection>? waitingPreview = null;
        try
        {
            await PdfCombinedQualificationRig.WaitForRequestAsync(outputPath, processing);
            waitingPreview = test.Content.InspectAsync(source, previewCancellation.Token);
            await Task.Delay(100);
            Assert.False(waitingPreview.IsCompleted);
            output = await test.PublishAsync("0000");
            publicationMilliseconds = timer.ElapsedMilliseconds;
            Assert.False(processing.IsCompleted, "The publication must complete while the large processing operation still owns its CPU slot.");
            previewCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingPreview);
            processed = await processing;
        }
        finally
        {
            previewCancellation.Cancel(); processingCancellation.Cancel();
            try { await processing; } catch (OperationCanceledException) { }
            if (waitingPreview is not null) try { await waitingPreview; } catch (OperationCanceledException) { }
        }
        var processingMilliseconds = timer.ElapsedMilliseconds;
        Assert.True(processed.Resources.PeakMemoryBytes > 0);
        Assert.InRange(processed.Resources.PeakMemoryBytes, 1, processed.Resources.MemoryLimitBytes);
        Assert.InRange(processed.Resources.CpuMilliseconds, 1, processed.Resources.CpuLimitMilliseconds);
        Assert.Equal(source.Sha256, processed.Receipt.SourceSha256);
        Assert.Equal(160, processed.Receipt.Pages.Count);
        await PdfCombinedQualificationRig.AssertIndexChecksumAsync(output.FilePath);

        // Request visibility occurs after real dependency probes and before the
        // resource-controlled Python launch. A 160-page render keeps that worker
        // active long enough to exercise cancellation after it started.
        var cancelledPath = Path.Combine(test.Root, "large-cancelled.pdf");
        using var cancellation = new CancellationTokenSource();
        var cancelling = test.Content.ExecuteAsync(source, cancelledPath, parameters, inspection, TimeSpan.FromMinutes(5), 128 * 1024 * 1024, cancellation.Token);
        var cancelTimer = new Stopwatch();
        try
        {
            await PdfCombinedQualificationRig.WaitForRequestAsync(cancelledPath, cancelling);
            await Task.Delay(200);
            Assert.False(cancelling.IsCompleted);
            cancelTimer.Start(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelling);
            cancelTimer.Stop();
        }
        finally
        {
            cancellation.Cancel();
            try { await cancelling; } catch (OperationCanceledException) { }
        }
        Assert.True(cancelTimer.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(source.Sha256, PdfCombinedQualificationRig.Sha(await File.ReadAllBytesAsync(source.StoragePath)));
        Assert.Equal(revision, await test.RevisionAsync("0000"));
        Assert.Equal(placements[0].DocumentId, (await test.Placements.GetAsync(placements[0].Id))!.DocumentId);
        var retryPath = Path.Combine(test.Root, "large-retry.pdf");
        var retry = await test.Content.ExecuteAsync(source, retryPath, parameters, inspection, TimeSpan.FromMinutes(5), 128 * 1024 * 1024);
        Assert.Equal(source.Sha256, retry.Receipt.SourceSha256);
        Assert.Equal(160, retry.Receipt.Pages.Count);
        Assert.Equal(retry.Receipt.OutputSha256, PdfCombinedQualificationRig.Sha(await File.ReadAllBytesAsync(retryPath)));
        await test.EvidenceAsync(new { scenario = "large-concurrent-publication-cancellation-retry", pages = 160,
            source.FileSize, publicationMilliseconds, processingMilliseconds, cancellationMilliseconds = cancelTimer.ElapsedMilliseconds,
            first = processed.Resources, retry = retry.Resources, output.FilePath, result = retryPath, formalQualification = false });
    }
}

internal sealed class PdfCombinedQualificationRig : IDisposable
{
    private static readonly JsonSerializerOptions CachedJsonOptions3 = new JsonSerializerOptions { WriteIndented = true };

    public static string Python => "C:/Program Files/Python311/python.exe";
    public static string Qpdf => Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
    public required string Root { get; init; }
    public required SubmissionApplication App { get; init; }
    public required SubmissionDocument Source { get; init; }
    public required SubmissionDocument Target { get; init; }
    public required InMemoryApplicationRepository Applications { get; init; }
    public required InMemoryDocumentRepository Documents { get; init; }
    public required InMemoryDocumentPlacementRepository Placements { get; init; }
    public required InMemoryWorkspaceRevisionStore Revisions { get; init; }
    public required InMemoryCtdNodeRepository Nodes { get; init; }
    public required InMemoryDocumentProcessingStore Versions { get; init; }
    public required InMemoryAssemblyStore AssemblyStore { get; init; }
    public required InMemoryPdfRenameStore Renames { get; init; }
    public required InMemoryPackageValidationStore Validations { get; init; }
    public required CtdNodeGraph Graph { get; init; }
    public required Guid NodeId { get; init; }
    public required AssemblyService Assembly { get; init; }
    public required DocumentProcessingService Processing { get; init; }
    public required PdfRenameService Rename { get; init; }
    public required PythonPdfContentEngine Content { get; init; }
    public required EctdPackageModelBuilder Builder { get; init; }
    public required FinalizationService Finalization { get; init; }
    public required Paths Policy { get; init; }
    public async Task<long> RevisionAsync(string sequence) => (await Revisions.GetRevisionAsync(App.Id, sequence))!.Value;

    public static async Task<PdfCombinedQualificationRig> CreateAsync(int pages = 4, bool large = false)
    {
        var root = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("RATOOLS_NATIVE_FIXTURE_ROOT") ?? ".artifacts/p509-joint", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(root, "0000", "uploads"));
        var app = new SubmissionApplication("000001", "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        app.CreateSequence("0000", "original", "P5 joint qualification").RevisePublishingMetadata(Metadata());
        var applications = new InMemoryApplicationRepository(); await applications.AddAsync(app);
        var documents = new InMemoryDocumentRepository(); var placements = new InMemoryDocumentPlacementRepository();
        var revisions = new InMemoryWorkspaceRevisionStore(applications); var finalizations = new InMemoryFinalizationStore();
        var nodes = new InMemoryCtdNodeRepository(revisions, placements, finalizations: finalizations);
        var graph = new CtdNodeGraph(app.Id, IchSectionDefinitions.Current);
        var quality = graph.Create("m3-quality", null, new Dictionary<string, string>());
        var body = graph.Create("m3-2-body-of-data", quality.Id, new Dictionary<string, string>());
        var substance = graph.Create("m3-2-s-drug-substance", body.Id, new Dictionary<string, string> { ["substance"] = "Drug", ["manufacturer"] = "Alpha" });
        var control = graph.Create("m3-2-s-4-control-of-drug-substance", substance.Id, new Dictionary<string, string>());
        var node = graph.Create("m3-2-s-4-1-specification", control.Id, new Dictionary<string, string>());
        await nodes.SaveSequenceAsync(graph, "0000", SequenceNodes(graph, "0000"), 0);
        var sourcePath = Path.Combine(root, "0000", "uploads", "source.pdf");
        var targetPath = Path.Combine(root, "0000", "uploads", "target.pdf");
        const string script = """
            import sys,pymupdf,random
            source,target,count,large=sys.argv[1:]; count=int(count)
            for path in (source,target):
                doc=pymupdf.open(); rng=random.Random(91)
                for i in range(count if path==source else 4):
                    page=doc.new_page(width=500,height=260)
                    page.insert_text((30,100),'Clinical report page '+str(i+1),fontsize=18)
                    if large=='1' and path==source:
                        pix=pymupdf.Pixmap(pymupdf.csRGB,256,256,rng.randbytes(256*256*3),False)
                        page.insert_image(pymupdf.Rect(200,120,300,220),pixmap=pix)
                doc.set_toc([[1,'Clinical overview',1],[2,'Results',2]])
                doc[0].insert_link({'kind':pymupdf.LINK_GOTO,'from':pymupdf.Rect(30,30,100,50),'page':1})
                if path==source:
                    doc[0].insert_link({'kind':pymupdf.LINK_GOTOR,'from':pymupdf.Rect(120,30,190,50),'file':'target.pdf','page':1,'to':pymupdf.Point(0,0)})
                doc.save(path)
            """;
        await ToolAsync(Python, ["-I", "-B", "-c", script, sourcePath, targetPath, pages.ToString(System.Globalization.CultureInfo.InvariantCulture), large ? "1" : "0"]);
        var source = await RegisterAsync(sourcePath, documents); var target = await RegisterAsync(targetPath, documents);
        var policy = new Paths(root); var boundary = new DocumentStorageBoundary(policy);
        var versions = new InMemoryDocumentProcessingStore(); var assemblies = new InMemoryAssemblyStore(documents, placements); var renames = new InMemoryPdfRenameStore(versions);
        var mutations = new WorkspaceMutationCoordinator(revisions, new InMemoryPersistenceTransaction(), finalizedGuard: new FinalizedWorkspaceGuard(finalizations));
        var processingOptions = new PdfProcessingExecutionOptions(); var files = new ProcessingFileStore(policy, processingOptions);
        var engine = new QpdfProcessingEngine(new() { ExecutablePath = Qpdf }); var inspector = new PdfPigPackageInspector();
        var content = new PythonPdfContentEngine(new() { Enabled = true, PythonExecutablePath = Python,
            ScriptPath = Path.GetFullPath("scripts/pdf_content_worker.py"), ScriptSha256 = Sha(await File.ReadAllBytesAsync("scripts/pdf_content_worker.py")),
            ScratchDirectory = Path.Combine(root, "probes"), TessdataDirectory = Path.GetFullPath(".artifacts/p508-qualification/tessdata") }, new() { ExecutablePath = Qpdf });
        var paths = new CtdNodePathResolver(new EctdWorkspacePathResolver());
        var assembly = new AssemblyService(new AssemblyPlanner(applications, documents, placements, nodes, boundary, revisions), assemblies,
            applications, documents, placements, nodes, versions, boundary, paths, new AssemblyFileStore(policy, boundary, new()), mutations, inspector, new());
        var processing = new DocumentProcessingService(versions, applications, documents, placements, mutations, boundary, files, engine, inspector, processingOptions, content);
        var rename = new PdfRenameService(new(applications, documents, placements, nodes, paths, mutations, files, versions, engine, inspector, processingOptions),
            renames, versions, applications, documents, placements, nodes, paths, mutations, files, engine, inspector, processingOptions);
        var profiles = new FdaEctd322StandardsProfileProvider();
        var builder = new EctdPackageModelBuilder(applications, placements, documents, profiles, boundary, nodes, revisions,
            new InMemoryApplicationImportStore(applications, documents, placements, nodes));
        var validations = new InMemoryPackageValidationStore();
        var captures = new ReleaseCaptureService(validations, new EctdReleaseCapturePlanner(builder, new IchIndexXmlWriter(), Registry(), profiles), new RetainedReleases(root));
        return new() { Root = root, App = app, Source = source, Target = target, Applications = applications, Documents = documents,
            Placements = placements, Revisions = revisions, Nodes = nodes, Versions = versions, AssemblyStore = assemblies, Renames = renames,
            Validations = validations, Graph = graph, NodeId = node.Id, Assembly = assembly, Processing = processing, Rename = rename,
            Content = content, Builder = builder, Finalization = new(finalizations, captures, revisions), Policy = policy };
    }

    private static SequenceNode[] SequenceNodes(CtdNodeGraph graph, string sequence) => graph.Nodes.Values.Select(node =>
        new SequenceNode(graph, sequence, node.Id, node.IdentityAttributes, null, 0,
            node.IdentityAttributes.ContainsKey("manufacturer") ? "alpha" : null)).ToArray();
    private static SequencePublishingMetadata Metadata() => SequencePublishingMetadata.Create("nda", "original-application", "application",
        "Synthetic joint qualification", "Synthetic Sponsor", null, "Test Contact", "regulatory", "5550100", "business", "synthetic@example.test");
    public async Task AddSequenceAsync(string sequence)
    {
        App.CreateSequence(sequence, "amendment", "Processed replacement").RevisePublishingMetadata(Metadata()); await Applications.UpdateAsync(App);
        await Nodes.SaveSequenceAsync(Graph, sequence, SequenceNodes(Graph, sequence), 0);
    }
    public async Task<DocumentPlacement[]> AssembleAsync(string sequence, SubmissionDocument[] documents, string operation = "new", IReadOnlyList<string>? history = null)
    {
        var csv = "sourceFile,nodeInstanceId,fileName,operation,title,sortOrder,targetLeafAddress" + string.Concat(documents.Select((document, index) =>
            $"\ndocument:{document.Id},{NodeId},{document.FileName},{operation},Clinical report,{index},{history?[index] ?? ""}"));
        var preview = await Assembly.PreviewAsync(App.Id, sequence, new(await RevisionAsync(sequence), csv));
        Assert.True(preview.CanExecute, JsonSerializer.Serialize(preview.Plan));
        var request = new AssemblyEnqueueRequest(preview.Id, preview.Plan.PlanDigest, "joint-assembly-" + preview.Id);
        var queued = await Assembly.EnqueueAsync(App.Id, sequence, request);
        await Assembly.ExecuteAsync((await AssemblyStore.ClaimAsync("joint", DateTime.UtcNow, TimeSpan.FromMinutes(3)))!);
        var job = await Assembly.JobAsync(App.Id, sequence, queued.Job.State.Id);
        Assert.Equal(PdfProcessingJobStatus.Completed, job.Status);
        Assert.Equal(queued.Job.State.Id, (await Assembly.EnqueueAsync(App.Id, sequence, request)).Job.State.Id);
        return await Task.WhenAll(job.Result!.Documents.OrderBy(document => document.LineNumber).Select(async document => (await Placements.GetAsync(document.PlacementId))!));
    }
    public async Task<SubmissionDocument> UploadAsync(string sequence, string fileName, string path)
    {
        await using var stream = File.OpenRead(path);
        var receipt = await Assembly.UploadSourceAsync(App.Id, sequence, await RevisionAsync(sequence), fileName, stream);
        return (await Documents.GetAsync(receipt.DocumentId))!;
    }
    public async Task<ProcessingJobView> ProcessAsync(string sequence, Guid placement, PdfProcessingParameters parameters)
    {
        var revision = await RevisionAsync(sequence);
        var inspected = await Processing.InspectAsync(App.Id, sequence, placement, revision);
        var plan = await Processing.CreatePlanAsync(App.Id, sequence, placement, new(inspected.SourceVersionId, revision, parameters));
        var queued = await Processing.EnqueueAsync(App.Id, sequence, plan.Id, plan.PlanDigest, "joint-processing-" + plan.Id);
        await Processing.ExecuteAsync((await Versions.ClaimAsync("joint", DateTime.UtcNow, TimeSpan.FromMinutes(5)))!);
        var job = await Processing.GetJobAsync(App.Id, sequence, queued.Job.Id);
        Assert.True(job.Status == PdfProcessingJobStatus.Completed, job.FailureCode + ": " + job.FailureMessage);
        await Processing.AdoptAsync(App.Id, sequence, job.Id, revision);
        return job;
    }
    public async Task<GeneratedBackboneDto> PublishAsync(string sequence)
    {
        var outputRoot = Path.Combine(Root, "publish-" + Guid.NewGuid().ToString("N"));
        // This fresh output root and retention=0 exclude both physical-deletion
        // branches in LocalBackboneFileWriter; each invocation gets a unique root.
        Assert.False(Directory.Exists(outputRoot));
        var service = new BackboneService(Builder, new IchIndexXmlWriter(), Registry(), new EctdXmlValidator(),
            new FdaEctd322StandardsProfileProvider(), new LocalBackboneFileWriter(Options.Create(new BackboneOutputOptions
            { RootPath = outputRoot, RetainJobRuns = 0 }), NullLogger<LocalBackboneFileWriter>.Instance));
        var result = await service.GenerateAsync(new(App.Id, sequence, Guid.NewGuid(), "report.json", "package.zip"));
        using var archive = ZipFile.OpenRead(result.PackagePath);
        var directory = Path.GetDirectoryName(result.FilePath)!;
        var paths = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray();
        Assert.Equal(paths.Length, archive.Entries.Count);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            using var entry = archive.GetEntry(relative)!.Open(); using var bytes = new MemoryStream(); await entry.CopyToAsync(bytes);
            Assert.Equal(await File.ReadAllBytesAsync(path), bytes.ToArray());
        }
        return result;
    }
    public async Task<(RegisteredPackageInput Input, PackageValidationRun Run, ValidationReport Report, PackageDeliveryInspection Inspection)> ValidateAsync(string sequence,
        GeneratedBackboneDto output, Guid? historyInputId = null)
    {
        var reader = new RetainedReader(); var root = Path.GetDirectoryName(output.FilePath)!;
        var registry = new PackageValidationInputRegistry(Validations, reader, Policy, Options.Create(new PackageValidationOptions()));
        var registered = await registry.RegisterAsync(new(App.Id, root, sequence));
        var id = registered.Id;
        var input = (await Validations.GetInputAsync(id))!;
        var captured = await reader.ReadAsync(new(App.Id, id.ToString("D"), root, sequence), new());
        var zipRegistered = await registry.RegisterAsync(new(App.Id, output.PackagePath, sequence));
        var zipId = zipRegistered.Id;
        var zipped = await reader.ReadAsync(new(App.Id, zipId.ToString("D"), output.PackagePath, sequence), new());
        Assert.Equal(captured.Manifest.Files, zipped.Manifest.Files);
        var service = new PackageValidationService(Validations, reader, Policy, new(new PdfPigPackageInspector()));
        var queued = await service.EnqueueAsync(new(id, PackageValidationCatalog.UsProfile, PackageValidationMode.Development, "joint-p2-" + id,
            ComparisonInputId: zipId,
            History: historyInputId is { } historical ? [new(historical, PackageValidationCatalog.UsProfile)] : []));
        var lease = (await Validations.ClaimAsync("joint", DateTime.UtcNow, TimeSpan.FromMinutes(5)))!;
        var report = await service.InspectAsync(lease.Run);
        var history = new List<ICapturedPackageInput>();
        if (historyInputId is { } historyId)
        {
            var previous = (await Validations.GetInputAsync(historyId))!;
            history.Add(await reader.ReadAsync(new(App.Id, historyId.ToString("D"), previous.SourcePath, previous.SequenceNumber), previous.Limits));
        }
        var baseline = new HistoryBaselineManifest(App.Id, sequence, history.Select(previous => new HistoryBaselineEntry(
            previous.Manifest.SequenceNumber, HistorySourceKind.ImportedExternal, previous.SourceId, previous.InputDigest,
            PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)));
        var inspection = await new PackageDeliveryInspector(new PdfPigPackageInspector()).InspectAsync(new(new(captured, baseline, history), zipped), PackageValidationCatalog.UsProfile);
        Assert.Equal(report.Binding.InputManifestDigest, inspection.InputDigest);
        Assert.Equal(report.Binding.HistoryBaselineDigest, inspection.HistoryManifestDigest);
        Assert.True(await Validations.CompleteAsync(lease.Run.Id, lease.Token, lease.Owner, DateTime.UtcNow, report));
        var run = (await Validations.GetRunAsync(queued.Run.Id))!;
        Assert.Equal(ValidationRunStatus.Completed, run.Status);
        await File.WriteAllTextAsync(Path.Combine(Root, "p2-" + sequence + ".json"), (await Validations.GetReportJsonAsync(run.Id))!);
        return (input, run, report, inspection);
    }
    public async Task<FinalizationResult> FreezeAsync(string sequence, PackageValidationRun run)
    {
        var profile = new FdaEctd322StandardsProfileProvider().GetProfile(App.EctdTemplateKey);
        return await Finalization.FinalizeAsync(new(App.Id, sequence, await RevisionAsync(sequence), run.Id,
            PackageValidationCatalog.UsProfile, profile.IchEctdVersion, profile.UsRegionalModule1Version,
            run.Binding.HistoryBaselineDigest, "joint-freeze-" + run.Id));
    }
    public static async Task<JsonElement> ReadPdfAsync(string path)
    {
        const string script = """
            import sys,pymupdf,json
            doc=pymupdf.open(sys.argv[1])
            print(json.dumps({'pages':len(doc),'text':''.join(p.get_text() for p in doc),'bookmarks':doc.get_toc(),
                'remoteFiles':[link.get('file') for p in doc for link in p.get_links() if link.get('kind')==pymupdf.LINK_GOTOR]}))
            """;
        using var parsed = JsonDocument.Parse(await ToolAsync(Python, ["-I", "-B", "-c", script, path])); return parsed.RootElement.Clone();
    }
    public static async Task AssertIndexChecksumAsync(string path)
    {
        var actual = Md5(await File.ReadAllBytesAsync(path));
        var checksum = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!, "index-md5.txt"));
        Assert.Equal(actual + Environment.NewLine, checksum);
    }
    public Task EvidenceAsync(object value) => File.WriteAllTextAsync(Path.Combine(Root, "evidence.json"), JsonSerializer.Serialize(value, CachedJsonOptions3));
    public static XDocument ReadIndex(string path) { using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }); return XDocument.Load(reader); }
    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Md5(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    private static RegionalBackboneWriterRegistry Registry() => new([new UsRegionalBackboneWriter(new UsRegionalXmlWriter())]);
    private static async Task<SubmissionDocument> RegisterAsync(string path, InMemoryDocumentRepository documents)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var document = new SubmissionDocument(Path.GetFileName(path), "application/pdf", bytes.Length, Sha(bytes), Md5(bytes), path);
        await documents.AddAsync(document); return document;
    }
    public static async Task<string> ToolAsync(string executable, string[] arguments)
    {
        using var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start(); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
    public static async Task WaitForRequestAsync(string outputPath, Task processing)
    {
        var timer = Stopwatch.StartNew();
        while (!File.Exists(outputPath + ".request.json") && !processing.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(20);
        Assert.True(File.Exists(outputPath + ".request.json"), "The real content worker request was not created.");
        Assert.False(processing.IsCompleted, "The content operation ended before concurrent work could start.");
    }
    public void Dispose() => Content.Dispose();

    public sealed class Paths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && full != root) throw new InvalidOperationException("Outside joint fixture.");
            return full;
        }
    }

    // The adapter retains bytes on disposal. Production P2 inspection/archival is
    // exercised, while PackageInputReader's physical-cleanup paths are excluded.
    private sealed class RetainedReader : IPackageInputReader
    {
        public Task<ICapturedPackageInput> ReadAsync(PackageInputSelection selection, PackageReadLimits limits, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(selection.Path))
            {
                var files = Directory.EnumerateFiles(selection.Path, "*", SearchOption.AllDirectories).ToDictionary(
                    path => selection.SequenceNumber + "/" + Path.GetRelativePath(selection.Path, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
                return Task.FromResult<ICapturedPackageInput>(new RetainedInput(selection, limits, files, null));
            }
            using var archive = ZipFile.OpenRead(selection.Path); var extracted = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                PackageLogicalPath.Validate(entry.FullName, limits.MaxPathLength, limits.MaxPathDepth);
                using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                extracted.Add(selection.SequenceNumber + "/" + entry.FullName, bytes.ToArray());
            }
            return Task.FromResult<ICapturedPackageInput>(new RetainedInput(selection, limits, extracted, Sha(File.ReadAllBytes(selection.Path))));
        }
    }
    private sealed class RetainedInput(PackageInputSelection selection, PackageReadLimits limits, Dictionary<string, byte[]> bytes, string? archiveSha256) : ICapturedPackageInput
    {
        public string SourceId => selection.SourceId;
        public PackageSourceKind SourceKind => archiveSha256 is null ? PackageSourceKind.Directory : PackageSourceKind.Zip;
        public string? ArchiveSha256 => archiveSha256;
        public PackageReadLimits Limits => limits;
        public PackageInputManifest Manifest { get; } = new(selection.ApplicationId, selection.SequenceNumber!, bytes.Select(file => new PackageInputFile(file.Key, file.Value.Length, Sha(file.Value), Md5(file.Value))), [], [], []);
        public string InputDigest => RATools.Domain.Common.CanonicalJson.Digest(JsonSerializer.SerializeToElement(new { schemaVersion = 1, manifestDigest = Manifest.Digest, archiveSha256 }));
        public Stream OpenRead(string logicalPath) => new MemoryStream(bytes[logicalPath], writable: false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            if (archiveSha256 is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Sha(File.ReadAllBytes(selection.Path)) != archiveSha256) throw new PackageInputChangedException(SourceId);
                return Task.CompletedTask;
            }
            foreach (var file in Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(selection.Path, file.LogicalPath[(selection.SequenceNumber!.Length + 1)..]);
                if (Sha(File.ReadAllBytes(path)) != file.Sha256) throw new PackageInputChangedException(SourceId);
            }
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // Real planner, P2 report, ReleaseCaptureService and FinalizationService feed
    // this deliberately retained disk provider. It verifies actual copied bytes
    // and canonical manifests, but does not qualify LocalReleaseCaptureStore.
    private sealed class RetainedReleases(string root) : IReleaseCaptureStore
    {
        private readonly Dictionary<string, ReleaseCaptureStaging> _published = [];
        public async Task<ReleaseCaptureStaging> SaveAsync(ReleaseCapturePlan plan, CancellationToken cancellationToken = default)
        {
            var directory = Path.Combine(root, "retained-release-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var measured = new List<ReleaseManifestFile>();
            foreach (var file in plan.Files)
            {
                ReleaseManifest.RequireRelativePath(file.RelativePath, nameof(plan));
                var bytes = file.Content ?? await File.ReadAllBytesAsync(file.SourcePath!, cancellationToken);
                Assert.Equal(file.Length, bytes.LongLength); Assert.Equal(file.Sha256, Sha(bytes)); Assert.Equal(file.Md5, Md5(bytes));
                var destination = Path.Combine(directory, file.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var stream = new FileStream(destination, FileMode.CreateNew)) await stream.WriteAsync(bytes, cancellationToken);
                measured.Add(new(file.RelativePath, bytes.LongLength, Sha(bytes), Md5(bytes), file.Kind));
            }
            return new(plan.ApplicationId, plan.SequenceNumber, directory, measured);
        }
        public async Task CompleteAsync(ReleaseCaptureStaging staging, ReleaseManifest manifest, CancellationToken cancellationToken = default)
        {
            Assert.Equal(staging.Files.OrderBy(file => file.RelativePath), manifest.Files.OrderBy(file => file.RelativePath));
            var path = Path.Combine(staging.Directory, "manifest.json"); await File.WriteAllBytesAsync(path, manifest.ToUtf8Json(), cancellationToken);
            _ = ReleaseManifest.FromJson(await File.ReadAllBytesAsync(path, cancellationToken), staging.ApplicationId, staging.SequenceNumber, manifest.Digest);
        }
        public Task<ReleaseCommitResult> CommitAsync(ReleaseCaptureStaging staging, string manifestDigest, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); _published.Add(manifestDigest, staging); return Task.FromResult(new ReleaseCommitResult(staging.Directory, true)); }
        public Task DiscardAsync(ReleaseCaptureStaging staging, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DiscardCommittedAsync(Guid applicationId, string sequenceNumber, string manifestDigest, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<ReleaseManifest?> ReadManifestAsync(Guid applicationId, string sequenceNumber, string manifestDigest, CancellationToken cancellationToken = default)
        {
            if (!_published.TryGetValue(manifestDigest, out var staging)) return null;
            return ReleaseManifest.FromJson(await File.ReadAllBytesAsync(Path.Combine(staging.Directory, "manifest.json"), cancellationToken), applicationId, sequenceNumber, manifestDigest);
        }
        public async Task<ReleaseFileContent> OpenFileAsync(Guid applicationId, string sequenceNumber, string manifestDigest, string relativePath, CancellationToken cancellationToken = default)
        {
            var manifest = (await ReadManifestAsync(applicationId, sequenceNumber, manifestDigest, cancellationToken))!;
            var file = manifest.Files.Single(candidate => candidate.RelativePath == relativePath); var path = Path.Combine(_published[manifestDigest].Directory, relativePath);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken); Assert.Equal(file.Sha256, Sha(bytes)); Assert.Equal(file.Length, bytes.LongLength);
            return new(new MemoryStream(bytes, writable: false), relativePath, file.Length, file.Sha256, "application/pdf");
        }
    }
}
