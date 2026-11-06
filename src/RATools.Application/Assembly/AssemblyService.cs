using System.Text;
using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.DocumentProcessing;
using RATools.Application.Documents;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Application.Assembly;

public sealed class AssemblyService(AssemblyPlanner planner, IAssemblyStore store, IApplicationRepository applications,
    IDocumentRepository documents, IDocumentPlacementRepository placements, ICtdNodeRepository nodes,
    IDocumentProcessingStore versions, IDocumentStorageBoundary boundary, CtdNodePathResolver paths,
    AssemblyFileStore files, WorkspaceMutationCoordinator mutations, IPackagePdfInspector inspector, AssemblyExecutionOptions options)
{
    public async Task<AssemblySourceUpload> UploadSourceAsync(Guid appId, string sequence, long expectedRevision,
        string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(appId, sequence, expectedRevision, cancellationToken).ConfigureAwait(false);
        var application = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var document = await files.ReceiveAsync(application, sequence, fileName, content, cancellationToken).ConfigureAwait(false);
        // Intake adds an unmounted source only. Holding the editable revision lock
        // prevents a finalize race; published graph revision remains unchanged.
        await documents.AddAsync(document, cancellationToken).ConfigureAwait(false);
        return Receipt(document, expectedRevision);
    }
    public async Task<IReadOnlyList<AssemblySourceUpload>> ListSourcesAsync(Guid appId, string sequence,
        CancellationToken cancellationToken = default)
    {
        var application = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var workspace = await nodes.GetSequenceAsync(appId, sequence, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException();
        return (await documents.ListAsync(cancellationToken).ConfigureAwait(false)).Where(document => files.IsSource(application, sequence, document))
            .OrderByDescending(document => document.CreatedUtc).ThenBy(document => document.Id).Select(document => Receipt(document, workspace.WorkspaceRevision)).ToArray();
    }
    public async Task<AssemblySourcePage> SourcePageAsync(Guid appId, string sequence, int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
    {
        PageBounds(offset, limit); var all = await ListSourcesAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        return new(all.Skip(offset).Take(limit).ToArray(), offset, limit, all.Count);
    }
    public async Task<AssemblyPreviewPage> PlanRowsAsync(Guid appId, string sequence, Guid planId, int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
    {
        PageBounds(offset, limit); var preview = await PlanAsync(appId, sequence, planId, cancellationToken).ConfigureAwait(false);
        return new(preview.Id, preview.Plan with { Rows = preview.Plan.Rows.Skip(offset).Take(limit).ToArray() }, preview.CreatedUtc,
            preview.ExpiresUtc, preview.ManifestSha256, preview.Format, preview.SelectedRows, preview.SelectionConfirmed, preview.CanExecute, offset, limit, preview.Plan.Rows.Count);
    }
    public async Task<AssemblyPreview> PreviewAsync(Guid appId, string sequence, AssemblyPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(appId, sequence, request.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        var application = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var bytes = Input(request);
        var parsed = AssemblyManifest.Parse(bytes, request.Format);
        var plan = await planner.PlanAsync(appId, sequence, request.ExpectedRevision, parsed, cancellationToken).ConfigureAwait(false);
        var workspace = await nodes.GetSequenceAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var rows = new List<AssemblyPlanRow>(); var sources = new List<AssemblySourceBinding>();
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in await placements.ListBySequenceAsync(appId, sequence, cancellationToken).ConfigureAwait(false))
        {
            if (placement.Operation == DocumentPlacementOperation.Delete) continue;
            var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false);
            if (document is not null) occupied.Add(PublishOutputNaming.BuildPublishedDocumentRelativePath(document, sequence));
        }
        long size = 0;
        foreach (var row in plan.Rows)
        {
            var issues = row.Diagnostics.ToList(); var updated = row;
            if (row.Status == AssemblyRowStatus.Valid && workspace is not null)
            {
                try
                {
                    var nodeId = Guid.Parse(row.NodeInstanceId!);
                    var nodeBinding = BindNode(workspace, nodeId);
                    var historical = row.TargetLeafAddress is null ? null : await placements.GetAsync(Guid.Parse(row.TargetLeafAddress), cancellationToken).ConfigureAwait(false);
                    var historyBinding = historical is null ? null : BindPlacement(historical);
                    if (historical is not null) await VerifyPlacementBytesAsync(application, historical, cancellationToken).ConfigureAwait(false);
                    if (row.Operation == "delete")
                    {
                        if (row.SourceFile is not null || row.FileName is not null)
                            throw new InvalidDataException("Delete lifecycle rows must not contain new file content or a filename.");
                    }
                    else
                    {
                        var document = await documents.GetAsync(Guid.Parse(row.DocumentId!), cancellationToken).ConfigureAwait(false)
                            ?? throw new InvalidDataException("The uploaded source document disappeared.");
                        _ = boundary.EnsureDocumentOwnedBySequence(document, application, sequence);
                        var fileName = row.FileName ?? document.FileName;
                        var href = paths.ResolveFile(application.EctdTemplateKey, workspace, nodeId, fileName);
                        if (!occupied.Add(href)) throw new InvalidDataException("The future delivery path collides with another row or current placement.");
                        size = checked(size + document.FileSize);
                        if (size > options.MaxBatchBytes) throw new InvalidDataException("The complete assembly input exceeds its batch size limit.");
                        await files.VerifyDocumentAsync(document, cancellationToken).ConfigureAwait(false);
                        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) Inspect(document.StoragePath, sequence + "/" + href, cancellationToken);
                        var capture = await files.CaptureAsync(application, sequence, document, cancellationToken).ConfigureAwait(false);
                        await versions.AddVersionAsync(capture, cancellationToken).ConfigureAwait(false);
                        updated = row with { FileName = fileName, SourceFile = "document:" + document.Id.ToString("D"),
                            SourceSha256 = capture.Sha256, SourceLength = capture.FileSize, TargetHref = href };
                        sources.Add(new(row.LineNumber, document.Id, capture.Id, updated.SourceFile!, fileName, document.MediaType,
                            capture.FileSize, capture.Sha256, capture.Md5, href, nodeBinding, historyBinding));
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
                { issues.Add(new("ASSEMBLY_SOURCE_OR_PATH_INVALID", error.Message, row.LineNumber)); }
            }
            rows.Add(updated with { Diagnostics = issues, Status = issues.Count == 0 ? AssemblyRowStatus.Valid : AssemblyRowStatus.Invalid });
        }
        plan = plan with { Rows = rows, ValidRows = rows.Count(row => row.Status == AssemblyRowStatus.Valid),
            InvalidRows = rows.Count(row => row.Status == AssemblyRowStatus.Invalid) };
        var valid = rows.Where(row => row.Status == AssemblyRowStatus.Valid).Select(row => row.LineNumber).Order().ToArray();
        var ready = valid.Length > 0 && plan.InvalidRows == 0 && GlobalValid(plan);
        var now = DateTime.UtcNow;
        var preview = new AssemblyPreview(Guid.NewGuid(), plan, now, now + options.PlanLifetime, AssemblyEvidence.Hash(bytes),
            request.Format, ready ? valid : [], sources, ready, ready, await InventoryAsync(appId, sequence, cancellationToken).ConfigureAwait(false), Convert.ToBase64String(bytes));
        if (ready) preview = await WithLinkDiagnosticsAsync(preview, cancellationToken).ConfigureAwait(false);
        preview = Seal(preview); await store.AddPlanAsync(preview, cancellationToken).ConfigureAwait(false); return preview;
    }

    public async Task<AssemblyPreview> SelectAsync(Guid appId, string sequence, Guid planId, AssemblySelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var original = await PlanAsync(appId, sequence, planId, cancellationToken).ConfigureAwait(false);
        if (original.Plan.ExpectedRevision != request.ExpectedRevision || original.Plan.PlanDigest != request.ParentDigest)
            throw new AssemblyPlanStaleException("The selection does not match its parent plan and revision.");
        await using var mutation = await mutations.AcquireAsync(appId, sequence, request.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        if (original.ExpiresUtc <= DateTime.UtcNow) throw new AssemblyPlanStaleException("The parent assembly preview expired; generate a new preview.");
        if (request.SelectedRows is null || request.SelectedRows.Count == 0 || request.SelectedRows.Distinct().Count() != request.SelectedRows.Count ||
            request.SelectedRows.Any(line => !original.Plan.Rows.Any(row => row.LineNumber == line && row.Status == AssemblyRowStatus.Valid)))
            throw new AssemblyPlanInvalidException("Choose a nonempty explicit subset of valid row identities.");
        var now = DateTime.UtcNow;
        var selectionPlan = original.Plan with { Diagnostics = original.Plan.Diagnostics.Where(issue => issue.Code != "ASSEMBLY_LINK_INVALID").ToArray() };
        var selected = original with { Id = Guid.NewGuid(), CreatedUtc = now, ExpiresUtc = now + options.PlanLifetime,
            SelectedRows = request.SelectedRows.Order().ToArray(), SelectionConfirmed = true,
            Plan = selectionPlan, CanExecute = GlobalValid(selectionPlan), ParentDigest = original.Plan.PlanDigest };
        await VerifyCurrentAsync(selected, cancellationToken).ConfigureAwait(false);
        selected = await WithLinkDiagnosticsAsync(selected, cancellationToken).ConfigureAwait(false);
        selected = Seal(selected); await store.AddPlanAsync(selected, cancellationToken).ConfigureAwait(false); return selected;
    }

    public async Task<AssemblyPreview> PlanAsync(Guid appId, string sequence, Guid id, CancellationToken cancellationToken = default)
    {
        var preview = await store.GetPlanAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Assembly preview was not found.");
        preview.Verify(); if (preview.Plan.ApplicationId != appId || preview.Plan.SequenceNumber != sequence)
            throw new KeyNotFoundException("Assembly preview belongs to another workspace.");
        return preview;
    }
    public async Task<AssemblyEnqueueResult> EnqueueAsync(Guid appId, string sequence, AssemblyEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        var plan = await PlanAsync(appId, sequence, request.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan.Plan.PlanDigest != request.PlanDigest) throw new AssemblyPlanStaleException("The reviewed assembly digest changed.");
        var state = AssemblyEvidence.Request(plan, request.IdempotencyKey);
        var existing = await store.GetPlanJobAsync(plan.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.State.RequestDigest != state.RequestDigest) throw new AssemblyPlanInvalidException("This plan already has a different execution request.");
            return new(existing, false);
        }
        await using var mutation = await mutations.AcquireAsync(appId, sequence, plan.Plan.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        await VerifyCurrentAsync(plan, cancellationToken).ConfigureAwait(false);
        if (!plan.CanExecute || !plan.SelectionConfirmed || plan.SelectedRows.Count == 0)
            throw new AssemblyPlanInvalidException("Confirm an explicit valid execution unit before enqueueing.");
        return await store.EnqueueAsync(state, cancellationToken).ConfigureAwait(false);
    }
    public async Task<AssemblyExecutionJobView?> PlanJobAsync(Guid appId, string sequence, Guid planId, CancellationToken cancellationToken = default)
    {
        _ = await PlanAsync(appId, sequence, planId, cancellationToken).ConfigureAwait(false);
        var job = await store.GetPlanJobAsync(planId, cancellationToken).ConfigureAwait(false); return job is null ? null : View(job);
    }
    public async Task<AssemblyExecutionJobView> JobAsync(Guid appId, string sequence, Guid id, CancellationToken cancellationToken = default)
    {
        var job = await store.GetJobAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Assembly execution was not found.");
        _ = await PlanAsync(appId, sequence, job.State.PlanId, cancellationToken).ConfigureAwait(false); return View(job);
    }
    public async Task<AssemblyExecutionJobView> CancelAsync(Guid appId, string sequence, Guid id, CancellationToken cancellationToken = default)
    {
        _ = await JobAsync(appId, sequence, id, cancellationToken).ConfigureAwait(false);
        return View(await store.CancelAsync(id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException());
    }
    public async Task ExecuteAsync(PdfProcessingLease lease, CancellationToken cancellationToken = default)
    {
        var plan = await store.GetPlanAsync(lease.Job.PlanId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Assembly preview is missing.");
        plan.Verify();
        if (plan.Plan.PlanDigest != lease.Job.PlanDigest || !plan.CanExecute) throw new InvalidDataException("Assembly execution binding changed.");
        await using var mutation = await mutations.AcquireAsync(plan.Plan.ApplicationId, plan.Plan.SequenceNumber, plan.Plan.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        await VerifyCurrentAsync(plan, cancellationToken).ConfigureAwait(false);
        var app = await ApplicationAsync(plan.Plan.ApplicationId, plan.Plan.SequenceNumber, cancellationToken).ConfigureAwait(false);
        var workspace = await nodes.GetSequenceAsync(app.Id, plan.Plan.SequenceNumber, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException();
        var prepared = new Dictionary<int, SubmissionDocument>();
        foreach (var source in plan.Sources.Where(item => plan.SelectedRows.Contains(item.LineNumber)))
        {
            var capture = await versions.GetVersionAsync(source.VersionId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Assembly source version is missing.");
            await files.VerifyAsync(capture, cancellationToken).ConfigureAwait(false);
            prepared[source.LineNumber] = await files.PrepareAsync(app, plan.Plan.SequenceNumber, lease.Job.Id, capture, source.TargetHref, cancellationToken).ConfigureAwait(false);
        }
        await ValidateLinksAsync(plan, cancellationToken).ConfigureAwait(false);
        await VerifyCurrentAsync(plan, cancellationToken).ConfigureAwait(false);
        await mutation.CommitAsync(async ct =>
        {
            var result = new List<AssemblyExecutionDocument>();
            foreach (var row in plan.Plan.Rows.Where(row => plan.SelectedRows.Contains(row.LineNumber)))
            {
                ct.ThrowIfCancellationRequested();
                var node = workspace.Nodes.Single(item => item.NodeInstanceId.ToString("D") == row.NodeInstanceId);
                var target = row.TargetLeafAddress is null ? null : await placements.GetAsync(Guid.Parse(row.TargetLeafAddress), ct).ConfigureAwait(false);
                var documentId = row.Operation == "delete" ? target!.DocumentId : prepared[row.LineNumber].Id;
                if (row.Operation != "delete") await documents.AddAsync(prepared[row.LineNumber], ct).ConfigureAwait(false);
                var placement = new DocumentPlacement(documentId, app.Id, plan.Plan.SequenceNumber, node.CtdSection,
                    Enum.Parse<DocumentPlacementOperation>(row.Operation, true), row.Title);
                placement.BindToNode(node, row.SortOrder);
                if (target is not null) placement.ReviseLifecycleTarget(target.Id);
                await placements.AddAsync(placement, ct).ConfigureAwait(false);
                var source = plan.Sources.SingleOrDefault(item => item.LineNumber == row.LineNumber);
                result.Add(new(row.LineNumber, placement.Id, documentId, source?.VersionId, source?.TargetHref, placement.LeafId, target?.Id));
            }
            var audit = new AssemblyExecutionAudit(lease.Job.Id, plan.Id, plan.Plan.PlanDigest, app.Id, plan.Plan.SequenceNumber,
                plan.Plan.ExpectedRevision, plan.Plan.ExpectedRevision + 1, DateTime.UtcNow, result);
            if (!await store.CompleteAsync(lease.Job.Id, lease.Token, lease.Owner, DateTime.UtcNow, audit, ct).ConfigureAwait(false))
                throw new AssemblyPlanStaleException("The assembly lease was lost or cancellation was requested.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyCurrentAsync(AssemblyPreview plan, CancellationToken cancellationToken)
    {
        if (plan.ExpiresUtc <= DateTime.UtcNow) throw new AssemblyPlanStaleException("The assembly preview expired.");
        if (await InventoryAsync(plan.Plan.ApplicationId, plan.Plan.SequenceNumber, cancellationToken).ConfigureAwait(false) != plan.InventoryDigest)
            throw new AssemblyPlanStaleException("Assembly inventory, node or historical target bindings changed.");
        foreach (var source in plan.Sources.Where(item => plan.SelectedRows.Contains(item.LineNumber)))
        {
            var document = await documents.GetAsync(source.DocumentId, cancellationToken).ConfigureAwait(false) ?? throw new AssemblyPlanStaleException("Assembly source was removed.");
            if (document.Sha256 != source.Sha256 || document.Md5 != source.Md5 || document.FileSize != source.Length)
                throw new AssemblyPlanStaleException("Assembly source metadata changed.");
            await files.VerifyDocumentAsync(document, cancellationToken).ConfigureAwait(false);
        }
        var app = await ApplicationAsync(plan.Plan.ApplicationId, plan.Plan.SequenceNumber, cancellationToken).ConfigureAwait(false);
        var history = plan.Plan.Rows.Where(row => plan.SelectedRows.Contains(row.LineNumber) && row.TargetLeafAddress is not null)
            .Select(row => Guid.Parse(row.TargetLeafAddress!)).ToHashSet();
        var allPlacements = await placements.ListByApplicationAsync(app.Id, cancellationToken).ConfigureAwait(false);
        var effectiveness = AssemblyPlanner.ReplayHistory(allPlacements.Where(placement =>
            string.CompareOrdinal(placement.SequenceNumber, plan.Plan.SequenceNumber) < 0));
        if (history.Any(target => effectiveness.GetValueOrDefault(target) != AssemblyPlanner.HistoricalLeafState.Current))
            throw new AssemblyPlanStaleException("A selected historical target is no longer effective or requires an explicit lifecycle policy.");
        // Link validation reads mounted PDF inventory; lifecycle events also depend
        // on target bytes, including a non-PDF delete target. Verify precisely those
        // dependencies against the metadata already bound into InventoryDigest.
        foreach (var placement in allPlacements)
        {
            var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false);
            if (history.Contains(placement.Id) || (placement.Operation != DocumentPlacementOperation.Delete && document?.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true))
                await VerifyPlacementBytesAsync(app, placement, cancellationToken).ConfigureAwait(false);
        }
    }
    private async Task<string> InventoryAsync(Guid appId, string sequence, CancellationToken cancellationToken)
    {
        var workspace = await nodes.GetSequenceAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var inventory = new List<object>();
        foreach (var placement in (await placements.ListByApplicationAsync(appId, cancellationToken).ConfigureAwait(false)).OrderBy(item => item.Id))
        {
            var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false);
            inventory.Add(new { Placement = BindPlacement(placement), Document = document is null ? null : new { document.Id, document.FileName,
                document.StoragePath, document.Sha256, document.Md5, document.FileSize } });
        }
        return AssemblyEvidence.Hash(JsonSerializer.Serialize(new { Inventory = inventory, Nodes = workspace?.Nodes.OrderBy(item => item.NodeInstanceId)
            .Select(item => BindNode(workspace, item.NodeInstanceId)).ToArray() }));
    }
    private async Task<AssemblyPreview> WithLinkDiagnosticsAsync(AssemblyPreview plan, CancellationToken cancellationToken)
    {
        if (!plan.CanExecute) return plan;
        try { await ValidateLinksAsync(plan, cancellationToken).ConfigureAwait(false); return plan; }
        catch (Exception error) when (error is InvalidDataException or PdfProcessingRejectedException)
        { return plan with { CanExecute = false, Plan = plan.Plan with { Diagnostics = [.. plan.Plan.Diagnostics, new("ASSEMBLY_LINK_INVALID", error.Message)] } }; }
    }
    private async Task ValidateLinksAsync(AssemblyPreview plan, CancellationToken cancellationToken)
    {
        var inventory = new List<PdfRenameDocument>();
        foreach (var source in plan.Sources.Where(item => plan.SelectedRows.Contains(item.LineNumber) && item.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
        {
            var version = await versions.GetVersionAsync(source.VersionId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException();
            inventory.Add(new(Guid.NewGuid(), source.DocumentId, source.VersionId, plan.Plan.SequenceNumber, source.TargetHref, source.Sha256, true,
                Inspect(version.StoragePath, plan.Plan.SequenceNumber + "/" + source.TargetHref, cancellationToken)));
        }
        foreach (var placement in await placements.ListByApplicationAsync(plan.Plan.ApplicationId, cancellationToken).ConfigureAwait(false))
        {
            if (placement.Operation == DocumentPlacementOperation.Delete) continue;
            var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false);
            if (document is null || !document.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            await files.VerifyDocumentAsync(document, cancellationToken).ConfigureAwait(false);
            var href = PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber);
            inventory.Add(new(placement.Id, document.Id, Guid.NewGuid(), placement.SequenceNumber, href, document.Sha256, false,
                Inspect(document.StoragePath, placement.SequenceNumber + "/" + href, cancellationToken)));
        }
        if (inventory.Count > 0 && PdfRenamePlanner.Create(plan.Plan.ApplicationId, plan.Plan.SequenceNumber, plan.Plan.ExpectedRevision, inventory, [], []).Issues.Count > 0)
            throw new InvalidDataException("The final assembly paths contain an unresolved, ambiguous or invalid PDF link.");
    }
    private PackagePdfFacts Inspect(string path, string href, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var facts = inspector.Inspect(stream, href, new PackageReadLimits { MaxFileBytes = options.MaxFileBytes }, cancellationToken);
        if (facts.Basics.ParseError is not null || facts.Basics.PageCount is not > 0 || !facts.NavigationComplete || !facts.DestinationsComplete)
            throw new InvalidDataException("The assembly PDF failed complete independent inspection.");
        return facts;
    }
    private async Task VerifyPlacementBytesAsync(SubmissionApplication application, DocumentPlacement placement, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("An assembly lifecycle or link target is missing.");
        _ = boundary.EnsureDocumentOwnedBySequence(document, application, placement.SequenceNumber);
        await files.VerifyDocumentAsync(document, cancellationToken).ConfigureAwait(false);
    }
    private static AssemblyPreview Seal(AssemblyPreview preview)
    {
        var digest = preview.ComputeDigest(); return preview with { Plan = preview.Plan with { PlanDigest = digest, Id = preview.Id,
            ExpiresUtc = preview.ExpiresUtc, ManifestSha256 = preview.ManifestSha256, SelectedRows = preview.SelectedRows } };
    }
    private static bool GlobalValid(AssemblyPlan plan) => plan.Diagnostics.All(issue => issue.Code == "ASSEMBLY_COLUMN_UNKNOWN");
    private static byte[] Input(AssemblyPreviewRequest request)
    {
        if (request.Format == "xlsx")
        {
            if (request.ManifestBase64 is null || request.ManifestBase64.Length > 2_800_000) throw new ArgumentException("The XLSX input exceeds its limit or is missing.");
            try { return Convert.FromBase64String(request.ManifestBase64); } catch (FormatException) { throw new ArgumentException("The workbook is not valid base64."); }
        }
        return Encoding.UTF8.GetBytes(request.Manifest ?? "");
    }
    private async Task<SubmissionApplication> ApplicationAsync(Guid appId, string sequence, CancellationToken cancellationToken)
    {
        var app = await applications.GetAsync(appId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException();
        if (app.Sequences.All(item => item.SequenceNumber != sequence)) throw new KeyNotFoundException(); return app;
    }
    private static string BindPlacement(DocumentPlacement placement) => JsonSerializer.Serialize(new { placement.Id, placement.DocumentId,
        placement.ApplicationId, placement.SequenceNumber, placement.NodeInstanceId, placement.CtdSection, placement.Operation,
        placement.SortOrder, placement.LeafId, placement.LifecycleTargetPlacementId, placement.Title });
    private static string BindNode(CtdSequenceWorkspace workspace, Guid id)
    {
        var node = workspace.Nodes.Single(item => item.NodeInstanceId == id);
        return JsonSerializer.Serialize(new { node.NodeInstanceId, node.CtdSection, node.DefinitionVersion, node.MetadataStatus,
            node.StorageSegment, node.Attributes, Definition = workspace.Graph.Get(id).DefinitionKey });
    }
    private static AssemblyExecutionJobView View(AssemblyExecutionJob job) => new(job.State.Id, job.State.PlanId, job.State.Status,
        job.State.Stage, job.State.AttemptCount, job.State.CancellationRequested, job.State.CreatedUtc, job.State.CompletedUtc,
        job.State.FailureCode, job.State.FailureMessage, job.Result);
    private static AssemblySourceUpload Receipt(SubmissionDocument document, long revision) => new(document.Id,
        "document:" + document.Id.ToString("D"), document.FileName, revision, document.FileSize, document.Sha256, document.Md5);
    private static void PageBounds(int offset, int limit)
    { if (offset < 0 || limit is < 1 or > AssemblyManifest.MaxRows) throw new ArgumentException("Use offset >= 0 and limit between 1 and 1000."); }
}
