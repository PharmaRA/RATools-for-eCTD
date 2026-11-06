using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.DocumentProcessing;
using RATools.Application.Documents;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfContentWorkflowTests
{
    [Theory]
    [InlineData("scan")]
    [InlineData("fonts")]
    [InlineData("version")]
    public async Task RealContentOperationHasBoundEvidenceAndIsAdoptedOnlyAtTheReviewedRevision(string mode)
    {
        using var test = await PdfContentTestRig.CreateAsync(mode);
        var before = await File.ReadAllBytesAsync(test.Document.StoragePath);
        var inspection = await test.InspectAsync();
        var content = await test.Service.InspectContentAsync(test.Application.Id, "0001", test.Placement.Id, new(inspection.SourceVersionId, 0));
        Assert.True(content.Capabilities.Enabled); Assert.Empty(content.BlockReasons);
        if (mode == "scan") { Assert.Equal(4, content.ScannedPages.Count); Assert.Empty(content.SearchablePages); }
        if (mode == "fonts") Assert.Equal(12, content.MissingFonts.Count);
        var parameters = PdfContentTestRig.Parameters(mode);
        var plan = await test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id,
            new(inspection.SourceVersionId, 0, new(null, null, null, Content: parameters)));
        Assert.Equal(content.Capabilities.EngineBinding, plan.EngineVersion);
        Assert.NotEqual(inspection.Navigation.EngineVersion, plan.EngineVersion);
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, "content-" + plan.Id);
        await test.Service.ExecuteAsync((await test.Store.ClaimAsync("content-test", DateTime.UtcNow, TimeSpan.FromMinutes(3)))!);
        var job = await test.Service.GetJobAsync(test.Application.Id, "0001", queued.Job.Id);
        Assert.Equal(PdfProcessingJobStatus.Completed, job.Status);
        Assert.Equal(test.Document.Id, (await test.Placements.GetAsync(test.Placement.Id))!.DocumentId);
        var receipt = await test.Service.GetContentReceiptAsync(test.Application.Id, "0001", job.Id);
        Assert.Equal(inspection.Sha256, receipt.Receipt.SourceSha256); Assert.Equal(job.ResultSha256, receipt.Receipt.OutputSha256);
        Assert.Equal("NotEvaluated", receipt.Receipt.RegulatoryConformance);
        Assert.True(receipt.Resources.PeakMemoryBytes > 0); Assert.True(receipt.Resources.CpuMilliseconds > 0);
        if (mode == "scan") { Assert.Equal((int[])[1, 2, 4], receipt.Receipt.Ocr!.ProcessedPages); Assert.True(receipt.Receipt.Ocr.RequiresTextReview); }
        if (mode == "fonts") Assert.Equal(12, receipt.Receipt.Fonts!.Embedded.Count);
        if (mode == "version") Assert.Equal("1.7", receipt.Receipt.OutputVersion);
        var adopted = await test.Service.AdoptAsync(test.Application.Id, "0001", job.Id, 0);
        var replay = await test.Service.AdoptAsync(test.Application.Id, "0001", job.Id, 0);
        Assert.Equal(adopted.DocumentId, replay.DocumentId); Assert.Equal(1, adopted.WorkspaceRevision);
        Assert.Equal(test.Placement.LeafId, (await test.Placements.GetAsync(test.Placement.Id))!.LeafId);
        Assert.Equal(before, await File.ReadAllBytesAsync(test.Document.StoragePath));
        Assert.Equal(job.Id, (await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, "content-" + plan.Id)).Job.Id);
        var version = (await test.Store.GetVersionAsync(job.ResultVersionId!.Value))!;
        Assert.Contains(receipt.ReceiptSha256, version.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultDisabledMissingModelAndUnknownFontsDoNotAdvertiseQualifiedOcrOrEmbedding()
    {
        using var disabled = new PythonPdfContentEngine(new(), new());
        Assert.False((await disabled.GetCapabilitiesAsync()).Enabled);
        using var test = await PdfContentTestRig.CreateAsync("fonts");
        test.Options.TessdataDirectory = null;
        var available = await test.Content.GetCapabilitiesAsync();
        Assert.True(available.Enabled); Assert.False(available.OcrAvailable); Assert.True(available.FontEmbeddingAvailable);
        var capture = await test.InspectAsync();
        await Assert.ThrowsAsync<PdfProcessingRejectedException>(() => test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id,
            new(capture.SourceVersionId, 0, new(null, null, null, Content: new(new("eng", [1]), false, null)))));
        test.Options.ScriptSha256 = new string('0', 64);
        Assert.False((await test.Content.GetCapabilitiesAsync()).Enabled);
        var blockedInspection = await test.Service.InspectContentAsync(test.Application.Id, "0001", test.Placement.Id,
            new(capture.SourceVersionId, 0));
        Assert.False(blockedInspection.Capabilities.Enabled);
        Assert.Contains("CONTENT_DEPENDENCY_OR_CONFIGURATION_NOT_QUALIFIED", blockedInspection.BlockReasons);
    }

    [Fact]
    public async Task ChangedLimitsInvalidatePlanAndTamperedReceiptCannotBeAdopted()
    {
        using var test = await PdfContentTestRig.CreateAsync("version");
        var capture = await test.InspectAsync();
        var plan = await test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id,
            new(capture.SourceVersionId, 0, new(null, null, null, Content: PdfContentTestRig.Parameters("version"))));
        test.Options.MaxPixels--;
        await Assert.ThrowsAsync<PdfProcessingRejectedException>(() => test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, "changed"));
        test.Options.MaxPixels++;
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, "receipt-tamper");
        await test.Service.ExecuteAsync((await test.Store.ClaimAsync("tamper", DateTime.UtcNow, TimeSpan.FromMinutes(3)))!);
        var result = (await test.Store.GetVersionAsync((await test.Store.GetJobAsync(queued.Job.Id))!.ResultVersionId!.Value))!;
        await File.AppendAllTextAsync(result.StoragePath + ".receipt.json", " ");
        await Assert.ThrowsAsync<InvalidDataException>(() => test.Service.AdoptAsync(test.Application.Id, "0001", queued.Job.Id, 0));
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Equal(test.Document.Id, (await test.Placements.GetAsync(test.Placement.Id))!.DocumentId);
    }

    [Fact]
    public async Task LegacyNavigationPlanJsonWithoutContentStillExecutesWithTheOriginalEngineVersion()
    {
        using var test = await PdfContentTestRig.CreateAsync("version");
        var capture = await test.InspectAsync();
        var current = await test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id,
            new(capture.SourceVersionId, 0, new("UseOutlines", null, null)));
        const string legacy = "{\"pageMode\":\"UseOutlines\",\"bookmarks\":null,\"links\":null}";
        var old = current with { Id = Guid.NewGuid(), ParametersJson = legacy,
            PlanDigest = PdfProcessingPlan.ComputeDigest(legacy, current.ApplicationId, current.SequenceNumber, current.PlacementId,
                current.SourceVersionId, current.SourceSha256, current.SourceMd5, current.EngineVersion,
                current.ExpectedWorkspaceRevision, current.ContextJson, current.SourceDocumentId, current.SourcePageCount) };
        await test.Store.AddPlanAsync(old);
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", old.Id, old.PlanDigest, "legacy-" + old.Id);
        await test.Service.ExecuteAsync((await test.Store.ClaimAsync("legacy", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!);
        Assert.Equal(PdfProcessingJobStatus.Completed, (await test.Store.GetJobAsync(queued.Job.Id))!.Status);
        Assert.Equal("qpdf-12.4.1", old.EngineVersion);
    }
}

internal sealed class PdfContentTestRig : IDisposable
{
    public required string Root { get; init; }
    public required SubmissionApplication Application { get; init; }
    public required SubmissionDocument Document { get; init; }
    public required DocumentPlacement Placement { get; init; }
    public required IDocumentRepository Documents { get; init; }
    public required IDocumentPlacementRepository Placements { get; init; }
    public required IDocumentProcessingStore Store { get; init; }
    public required IWorkspaceRevisionStore Revisions { get; init; }
    public required PdfContentEngineOptions Options { get; init; }
    public required PythonPdfContentEngine Content { get; init; }
    public required DocumentProcessingService Service { get; init; }
    public static string Python => "C:/Program Files/Python311/python.exe";
    public static string Qpdf => Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
    public Task<ProcessingInspection> InspectAsync() => Service.InspectAsync(Application.Id, "0001", Placement.Id, 0);
    public static PdfContentParameters Parameters(string mode) => mode switch
    {
        "scan" => new(new("eng", [1, 2, 4]), false, null), "fonts" => new(null, true, null), _ => new(null, false, "1.7")
    };
    public static async Task<PdfContentTestRig> CreateAsync(string mode)
    {
        var root = Path.GetFullPath(".artifacts/p508-csharp-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "0001", "m3"));
        var path = Path.Combine(root, "0001", "m3", "source.pdf");
        await CreateFixtureAsync(mode, path);
        var application = new SubmissionApplication("p508-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "Content processing test");
        var applications = new InMemoryApplicationRepository(); await applications.AddAsync(application);
        var documents = new InMemoryDocumentRepository(); var placements = new InMemoryDocumentPlacementRepository();
        var bytes = await File.ReadAllBytesAsync(path);
        var document = new SubmissionDocument("source.pdf", "application/pdf", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path); await documents.AddAsync(document);
        var placement = new DocumentPlacement(document.Id, application.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, "Content source");
        await placements.AddAsync(placement);
        var revisions = new InMemoryWorkspaceRevisionStore(applications); var store = new InMemoryDocumentProcessingStore();
        var options = OptionsFor(root); var content = new PythonPdfContentEngine(options, new() { ExecutablePath = Qpdf });
        var processing = new PdfProcessingExecutionOptions(); var paths = new Paths(root);
        var service = new DocumentProcessingService(store, applications, documents, placements,
            new WorkspaceMutationCoordinator(revisions, new InMemoryPersistenceTransaction()), new DocumentStorageBoundary(paths),
            new ProcessingFileStore(paths, processing), new QpdfProcessingEngine(new() { ExecutablePath = Qpdf }), new PdfPigPackageInspector(), processing, content);
        return new() { Root = root, Application = application, Document = document, Placement = placement, Documents = documents, Placements = placements,
            Store = store, Revisions = revisions, Options = options, Content = content, Service = service };
    }
    public static PdfContentEngineOptions OptionsFor(string root) => new()
    {
        Enabled = true, PythonExecutablePath = Python, ScriptPath = Path.GetFullPath("scripts/pdf_content_worker.py"),
        ScriptSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("scripts/pdf_content_worker.py"))).ToLowerInvariant(),
        ScratchDirectory = Path.Combine(root, "host-probes"), TessdataDirectory = Path.GetFullPath(".artifacts/p508-qualification/tessdata")
    };
    public static async Task CreateFixtureAsync(string mode, string path)
    {
        var script = """
            import sys,pymupdf
            from pathlib import Path
            mode,path=sys.argv[1:]
            image=pymupdf.open(); p=image.new_page(width=500,height=260)
            p.insert_text((50,100),'Clinical study report 12345',fontsize=20)
            p.insert_text((50,150),'Results and conclusions 67890',fontsize=20)
            doc=pymupdf.open()
            fonts=['helv','hebo','heit','hebi','tiro','tibo','tiit','tibi','cour','cobo','coit','cobi'] if mode=='fonts' else ['helv']*4
            for i,font in enumerate(fonts):
                p=doc.new_page(width=500,height=260)
                if mode=='scan': p.insert_image(p.rect,pixmap=image[0].get_pixmap(dpi=150))
                else: p.insert_text((30,100),'Clinical report Cafe 12345',fontsize=18,fontname=font)
                if mode=='scan':
                    if i%2:p.set_rotation(90 if i==1 else 270)
                    if i>1:p.set_cropbox(pymupdf.Rect(20,20,480,240))
            doc.set_toc([[1,'Clinical overview',1],[2,'Results',2]])
            doc[0].insert_link({'kind':pymupdf.LINK_GOTO,'from':pymupdf.Rect(30,30,100,50),'page':1})
            doc.save(path)
            """;
        await RunToolAsync(Python, ["-I", "-B", "-c", script, mode, path]);
        if (mode == "version")
        {
            var original = path + ".original.pdf"; File.Copy(path, original);
            // Write a fresh older-version fixture; never overwrite or remove an existing test file.
            var old = path + ".v14.pdf"; await RunToolAsync(Qpdf, [original, "--object-streams=disable", "--force-version=1.4", old]);
            // SubmissionDocument will consume this byte representation; the original fixture remains retained.
            await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(old));
        }
    }
    public static async Task RunToolAsync(string executable, string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start(); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        Assert.True(process.ExitCode == 0, await stderr); await stdout;
    }
    public void Dispose() => Content.Dispose();
    public sealed class Paths(string root) : IWorkspacePathPolicy
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
