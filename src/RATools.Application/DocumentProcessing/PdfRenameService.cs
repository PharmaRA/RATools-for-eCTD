using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing;
using RATools.Application.Workspaces;
using RATools.Domain.Documents;
using RATools.Domain.Applications;

namespace RATools.Application.DocumentProcessing;

public sealed class PdfRenameService(
    PdfRenamePreviewService previews,
    IPdfRenameStore renameStore,
    IDocumentProcessingStore versions,
    IApplicationRepository applications,
    IDocumentRepository documents,
    IDocumentPlacementRepository placements,
    ICtdNodeRepository nodes,
    CtdNodePathResolver paths,
    WorkspaceMutationCoordinator mutations,
    ProcessingFileStore files,
    IPdfProcessingEngine engine,
    IPackagePdfInspector inspector,
    PdfProcessingExecutionOptions options,
    IAuditLogRepository? audit = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PdfRenamePreview> CreatePreviewAsync(Guid applicationId, string sequence,
        PdfRenamePreviewRequest request, CancellationToken cancellationToken = default)
    {
        var preview = await previews.PreviewAsync(applicationId, sequence, request.ExpectedRevision,
            request.Changes, request.ConfirmedSources, cancellationToken).ConfigureAwait(false);
        await renameStore.AddPlanAsync(preview, cancellationToken).ConfigureAwait(false);
        return preview;
    }

    public async Task<PdfRenamePreview> GetPreviewAsync(Guid applicationId, string sequence, Guid planId,
        CancellationToken cancellationToken = default)
    {
        var preview = await RequirePreviewAsync(applicationId, sequence, planId, cancellationToken).ConfigureAwait(false);
        return preview;
    }

    public async Task<PdfRenameJobView?> GetPlanJobAsync(Guid applicationId, string sequence, Guid planId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequirePreviewAsync(applicationId, sequence, planId, cancellationToken).ConfigureAwait(false);
        var job = await renameStore.GetPlanJobAsync(planId, cancellationToken).ConfigureAwait(false);
        return job is null ? null : await ViewAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PdfRenameEnqueueResult> EnqueueAsync(Guid applicationId, string sequence, Guid planId,
        string planDigest, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var preview = await RequirePreviewAsync(applicationId, sequence, planId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(preview.Plan.PlanDigest, planDigest, StringComparison.Ordinal))
            throw new PdfProcessingRejectedException("The reviewed PDF rename plan digest changed.");
        var candidate = PdfRenameRequests.Create(preview, idempotencyKey);
        // Replay is deliberately checked before revision and expiry checks. A lost
        // response after enqueue remains recoverable even when the workspace moved on.
        var existing = await renameStore.GetPlanJobAsync(planId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.State.RequestDigest != candidate.RequestDigest)
                throw new PdfProcessingIdempotencyConflictException(idempotencyKey);
            return new(existing, false);
        }
        if (preview.ExpiresUtc <= DateTime.UtcNow) throw new PdfProcessingRejectedException("The PDF rename preview expired; create a new preview.");
        if (!preview.Plan.CanExecute) throw new PdfProcessingRejectedException("Resolve all PDF rename issues and explicitly confirm every required source.");
        await using var mutation = await mutations.AcquireAsync(applicationId, sequence, preview.Plan.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        await ValidateInventoryAsync(preview, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        return await renameStore.EnqueueAsync(candidate, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PdfRenameJobView> GetJobAsync(Guid applicationId, string sequence, Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await renameStore.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The PDF rename job was not found.");
        _ = await RequirePreviewAsync(applicationId, sequence, job.State.PlanId, cancellationToken).ConfigureAwait(false);
        return await ViewAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PdfRenameJobView> CancelAsync(Guid applicationId, string sequence, Guid jobId,
        CancellationToken cancellationToken = default)
    {
        _ = await GetJobAsync(applicationId, sequence, jobId, cancellationToken).ConfigureAwait(false);
        var job = await renameStore.CancelAsync(jobId, DateTime.UtcNow, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The PDF rename job was not found.");
        return await ViewAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessingContent> OpenContentAsync(Guid applicationId, string sequence, Guid jobId, Guid placementId,
        CancellationToken cancellationToken = default)
    {
        var job = await renameStore.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The PDF rename job was not found.");
        _ = await RequirePreviewAsync(applicationId, sequence, job.State.PlanId, cancellationToken).ConfigureAwait(false);
        var output = job.Results.SingleOrDefault(item => item.PlacementId == placementId)
            ?? throw new KeyNotFoundException("The PDF rename output was not found.");
        var version = await versions.GetVersionAsync(output.VersionId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The PDF rename output version was not found.");
        await files.VerifyAsync(version, cancellationToken).ConfigureAwait(false);
        return new(version.StoragePath, Path.GetFileName(output.Href), version.FileSize, version.Sha256);
    }

    public async Task ExecuteAsync(PdfProcessingLease lease, CancellationToken cancellationToken = default)
    {
        var job = await renameStore.GetJobAsync(lease.Job.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The PDF rename job is missing.");
        var preview = await VerifyLeasePreviewAsync(job, cancellationToken).ConfigureAwait(false);
        if (job.State.LeaseToken != lease.Token || job.State.LeaseOwner != lease.Owner || job.State.LeaseExpiresUtc <= DateTime.UtcNow || job.State.CancellationRequested)
            throw new PdfProcessingRejectedException("The PDF rename lease is no longer owned by this worker.");
        if (preview.EngineVersion != engine.EngineVersion || !preview.Plan.CanExecute)
            throw new PdfProcessingRejectedException("The processing engine or approved scope changed.");
        await using var mutation = await mutations.AcquireAsync(preview.Plan.ApplicationId, preview.Plan.SequenceNumber,
            preview.Plan.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        await ValidateInventoryAsync(preview, preview.Plan.ApplicationId, preview.Plan.SequenceNumber, cancellationToken).ConfigureAwait(false);
        var application = await applications.GetAsync(preview.Plan.ApplicationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The application was not found.");
        var outputs = new List<PdfRenameOutput>();
        foreach (var placementId in preview.Plan.RequiredSources.Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = preview.Sources.Single(item => item.PlacementId == placementId);
            var sourceVersion = await versions.GetVersionAsync(source.VersionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("A captured PDF source version is missing.");
            await files.VerifyAsync(sourceVersion, cancellationToken).ConfigureAwait(false);
            var edits = preview.Plan.Impacts.Where(item => item.SourcePlacementId == placementId)
                .OrderBy(item => item.ObjectPath, StringComparer.Ordinal)
                .Select(item => new PdfReferenceEdit(item.SourcePageNumber!.Value, item.ObjectPath,
                    item.OldReference, item.NewReference, item.IsUri)).ToArray();
            var parameters = new PdfProcessingParameters(null, null, null, edits);
            var outputPath = files.CreateAttemptOutput(application, lease.Job.Id, Guid.NewGuid());
            var result = await engine.ExecuteAsync(new(sourceVersion.StoragePath, outputPath, parameters,
                options.ExecutionTimeout, 64 * 1024 * 1024, options.MaxFileBytes), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded) throw new PdfProcessingRejectedException(result.ErrorMessage ?? "The PDF engine did not produce a result.");
            await files.VerifyAsync(sourceVersion, cancellationToken).ConfigureAwait(false);
            var measured = await files.MeasureAsync(outputPath, cancellationToken).ConfigureAwait(false);
            var finalHref = preview.Plan.Changes.SingleOrDefault(item => item.PlacementId == placementId)?.NewHref ?? source.Href;
            ValidateOutput(sourceVersion, source, outputPath, finalHref, edits, cancellationToken);
            var version = DocumentVersion.CreateResult(preview.Plan.ApplicationId, preview.Plan.SequenceNumber, source.DocumentId,
                source.VersionId, Path.GetFileName(finalHref), sourceVersion.MediaType, measured.Length, measured.Sha256,
                measured.Md5, outputPath, DateTime.UtcNow, engine.EngineVersion, JsonSerializer.Serialize(parameters, Json),
                preview.Plan.PlanDigest);
            await files.VerifyAsync(version, cancellationToken).ConfigureAwait(false);
            await versions.AddVersionAsync(version, cancellationToken).ConfigureAwait(false);
            outputs.Add(new(placementId, version.Id, finalHref, version.Sha256, version.Md5));
        }
        await ValidateFinalGraphAsync(preview, outputs, cancellationToken).ConfigureAwait(false);
        await ValidateInventoryAsync(preview, preview.Plan.ApplicationId, preview.Plan.SequenceNumber, cancellationToken).ConfigureAwait(false);
        if (!await renameStore.CompleteAsync(lease.Job.Id, lease.Token, lease.Owner, DateTime.UtcNow, outputs, cancellationToken).ConfigureAwait(false))
            throw new PdfProcessingRejectedException("The PDF rename lease was lost or cancellation was requested.");
    }

    public async Task<PdfRenameAdoption> AdoptAsync(Guid applicationId, string sequence, Guid jobId,
        long expectedRevision, CancellationToken cancellationToken = default)
    {
        var job = await renameStore.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The PDF rename job was not found.");
        var preview = await RequirePreviewAsync(applicationId, sequence, job.State.PlanId, cancellationToken).ConfigureAwait(false);
        var replay = await renameStore.GetAdoptionAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (replay is not null)
        {
            if (replay.ExpectedRevision != expectedRevision) throw new PdfProcessingRejectedException("The workspace revision changed; this adoption cannot be replayed.");
            return replay;
        }
        if (job.State.Status != PdfProcessingJobStatus.Completed) throw new PdfProcessingRejectedException("Only a completed PDF rename job can be adopted.");
        if (expectedRevision != preview.Plan.ExpectedRevision) throw new PdfProcessingRejectedException("Adoption must use the reviewed workspace revision.");
        WorkspaceMutation acquired;
        try { acquired = await mutations.AcquireAsync(applicationId, sequence, expectedRevision, cancellationToken).ConfigureAwait(false); }
        catch (WorkspaceRevisionConflictException)
        {
            replay = await renameStore.GetAdoptionAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (replay is not null && replay.ExpectedRevision == expectedRevision) return replay;
            throw;
        }
        await using var mutation = acquired;
        replay = await renameStore.GetAdoptionAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return replay;
        await ValidateInventoryAsync(preview, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        await ValidateFinalGraphAsync(preview, job.Results, cancellationToken).ConfigureAwait(false);
        var workspace = preview.Plan.Changes.Any(item => item.TargetNodeInstanceId is not null)
            ? await nodes.GetSequenceAsync(applicationId, sequence, cancellationToken).ConfigureAwait(false) : null;
        var drafts = new List<(DocumentPlacement Placement, SubmissionDocument Draft, PdfRenameOutput Output)>();
        foreach (var output in job.Results.OrderBy(item => item.PlacementId))
        {
            var placement = await placements.GetAsync(output.PlacementId, cancellationToken).ConfigureAwait(false)
                ?? throw new PdfProcessingRejectedException("A renamed placement was removed.");
            var version = await versions.GetVersionAsync(output.VersionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("A renamed result version is missing.");
            await files.VerifyAsync(version, cancellationToken).ConfigureAwait(false);
            var draft = await files.MaterializeAsync(await RequireApplicationAsync(applicationId, cancellationToken), sequence,
                jobId, version, output.Href, cancellationToken).ConfigureAwait(false);
            var change = preview.Plan.Changes.SingleOrDefault(item => item.PlacementId == placement.Id);
            var replacement = DocumentPlacement.Rehydrate(placement.Id, draft.Id, placement.ApplicationId, placement.SequenceNumber,
                placement.CtdSection, placement.Operation, placement.Title, placement.LifecycleTargetPlacementId,
                placement.CreatedUtc, placement.LeafId, placement.NodeInstanceId, placement.SortOrder, placement.ImportedSource);
            if (change?.TargetNodeInstanceId is { } targetId)
            {
                var node = workspace?.Nodes.SingleOrDefault(item => item.NodeInstanceId == targetId)
                    ?? throw new PdfProcessingRejectedException("The target business node is no longer present.");
                replacement.BindToNode(node, placement.SortOrder);
            }
            drafts.Add((replacement, draft, output));
        }
        var adoption = new PdfRenameAdoption(jobId, applicationId, sequence, expectedRevision, expectedRevision + 1,
            DateTime.UtcNow, drafts.Select(item => new PdfRenameAdoptedDocument(item.Output.PlacementId, item.Draft.Id,
                item.Output.VersionId, item.Output.Href, item.Placement.NodeInstanceId)).ToArray());
        await mutation.CommitAsync(async ct =>
        {
            foreach (var item in drafts)
            {
                await documents.AddAsync(item.Draft, ct).ConfigureAwait(false);
                if (!await placements.UpdateAsync(item.Placement, ct).ConfigureAwait(false))
                    throw new PdfProcessingRejectedException("A renamed placement no longer exists.");
            }
            await renameStore.AddAdoptionAsync(adoption, ct).ConfigureAwait(false);
            if (audit is not null)
            {
                var entry = new RATools.Domain.Auditing.AuditLogEntry("PdfRenameBatch", jobId.ToString("D"), "Adopt", "workspace-user");
                entry.AddDetails(JsonSerializer.Serialize(new { preview.Plan.PlanDigest, adoption.ExpectedRevision,
                    adoption.WorkspaceRevision, adoption.Documents }, Json));
                await audit.AddAsync(entry, ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
        return adoption;
    }

    private async Task<PdfRenamePreview> VerifyLeasePreviewAsync(PdfRenameJob job, CancellationToken cancellationToken)
    {
        var preview = await RequirePreviewAsync(job.State.PlanId, cancellationToken).ConfigureAwait(false);
        if (job.State.PlanDigest != preview.Plan.PlanDigest || job.State.Status != PdfProcessingJobStatus.Running)
            throw new InvalidDataException("The PDF rename job binding changed.");
        return preview;
    }

    private async Task<PdfRenamePreview> RequirePreviewAsync(Guid applicationId, string sequence, Guid planId,
        CancellationToken cancellationToken)
    {
        var preview = await RequirePreviewAsync(planId, cancellationToken).ConfigureAwait(false);
        if (preview.Plan.ApplicationId != applicationId || preview.Plan.SequenceNumber != sequence)
            throw new KeyNotFoundException("The PDF rename preview is not in this workspace.");
        return preview;
    }

    private async Task<PdfRenamePreview> RequirePreviewAsync(Guid planId, CancellationToken cancellationToken)
    {
        var preview = await renameStore.GetPlanAsync(planId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The PDF rename preview was not found.");
        preview.Verify();
        return preview;
    }

    private async Task<SubmissionApplication> RequireApplicationAsync(Guid id, CancellationToken cancellationToken) =>
        await applications.GetAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException("The application was not found.");

    private async Task ValidateInventoryAsync(PdfRenamePreview preview, Guid applicationId, string sequence,
        CancellationToken cancellationToken)
    {
        var current = (await placements.ListByApplicationAsync(applicationId, cancellationToken).ConfigureAwait(false))
            .Where(item => item.Operation != DocumentPlacementOperation.Delete)
            .ToArray();
        var expected = preview.Inventory;
        var currentBindings = new List<PdfRenamePlacementBinding>();
        foreach (var placement in current)
        {
            var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false);
            if (document is null) throw new PdfProcessingRejectedException("An inventory document was removed.");
            currentBindings.Add(PdfRenamePreviewService.Bind(placement, document));
            var measured = await files.MeasureAsync(document.StoragePath, cancellationToken).ConfigureAwait(false);
            if (measured.Sha256 != document.Sha256 || measured.Md5 != document.Md5 || measured.Length != document.FileSize)
                throw new PdfProcessingRejectedException("A captured delivery file changed on disk.");
        }
        if (currentBindings.Count != expected.Count || currentBindings.Select(item => item.PlacementId).Order().SequenceEqual(expected.Select(item => item.PlacementId).Order()) is false)
            throw new PdfProcessingRejectedException("The PDF inventory changed; regenerate the rename preview.");
        foreach (var item in expected)
        {
            var actual = currentBindings.Single(value => value.PlacementId == item.PlacementId);
            if (actual != item)
                throw new PdfProcessingRejectedException("A PDF source or placement changed; regenerate the rename preview.");
        }
        if (preview.Plan.Changes.Any(item => item.TargetNodeInstanceId is not null))
        {
            var application = await RequireApplicationAsync(applicationId, cancellationToken).ConfigureAwait(false);
            var workspace = await nodes.GetSequenceAsync(applicationId, sequence, cancellationToken).ConfigureAwait(false)
                ?? throw new PdfProcessingRejectedException("The business node workspace disappeared.");
            foreach (var change in preview.Plan.Changes.Where(item => item.TargetNodeInstanceId is not null))
            {
                var node = workspace.Nodes.SingleOrDefault(item => item.NodeInstanceId == change.TargetNodeInstanceId);
                if (node is null || !node.AllowsLeaves || paths.ResolveFile(application.EctdTemplateKey, workspace,
                    node.NodeInstanceId, Path.GetFileName(change.NewHref)) != change.NewHref)
                    throw new PdfProcessingRejectedException("A target business node or its delivery path changed.");
                var source = current.Single(item => item.Id == change.PlacementId);
                if (source.NodeInstanceId != node.NodeInstanceId && (source.Operation != DocumentPlacementOperation.New ||
                    current.Any(item => item.LifecycleTargetPlacementId == source.Id)))
                    throw new PdfProcessingRejectedException("The business move conflicts with lifecycle history.");
            }
        }
    }

    private void ValidateOutput(DocumentVersion source, PdfRenameSource binding, string output,
        string finalHref, IReadOnlyList<PdfReferenceEdit> edits, CancellationToken cancellationToken)
    {
        var facts = Inspect(output, binding.SequenceNumber + "/" + finalHref, cancellationToken);
        var original = Inspect(source.StoragePath, binding.SequenceNumber + "/" + binding.Href, cancellationToken);
        if (facts.Basics.PageCount != original.Basics.PageCount || facts.Basics.HasSearchableText != original.Basics.HasSearchableText ||
            facts.Basics.AllFontsEmbedded != original.Basics.AllFontsEmbedded ||
            !facts.Destinations.SequenceEqual(original.Destinations) || facts.Links.Count != original.Links.Count)
            throw new PdfProcessingRejectedException("A renamed PDF changed unselected page or destination facts.");
        foreach (var edit in edits)
        {
            var link = facts.Links.SingleOrDefault(item => item.ObjectPath == edit.ObjectPath)
                ?? throw new PdfProcessingRejectedException("A rewritten PDF link disappeared from the result.");
            var reference = edit.IsUri ? link.Target.Uri : link.Target.FileSpecification;
            var previous = original.Links.Single(item => item.ObjectPath == edit.ObjectPath);
            if (!string.Equals(reference, edit.NewReference, StringComparison.Ordinal) ||
                link.Target != (edit.IsUri ? previous.Target with { Uri = edit.NewReference } : previous.Target with { FileSpecification = edit.NewReference }))
                throw new PdfProcessingRejectedException("A rewritten PDF link does not match its reviewed final target.");
        }
        var selected = edits.Select(item => item.ObjectPath).ToHashSet(StringComparer.Ordinal);
        if (!facts.Links.Where(item => !selected.Contains(item.ObjectPath)).SequenceEqual(original.Links.Where(item => !selected.Contains(item.ObjectPath))))
            throw new PdfProcessingRejectedException("An unselected PDF navigation action changed.");
    }

    private PackagePdfFacts Inspect(string path, string logical, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var facts = inspector.Inspect(stream, logical, new PackageReadLimits { MaxFileBytes = options.MaxFileBytes }, cancellationToken);
        if (facts.Basics.ParseError is not null || !facts.NavigationComplete || !facts.DestinationsComplete || facts.Basics.PageCount is not > 0)
            throw new PdfProcessingRejectedException("A PDF failed complete P2 inspection.");
        return facts;
    }

    private async Task ValidateFinalGraphAsync(PdfRenamePreview preview, IReadOnlyList<PdfRenameOutput> outputs, CancellationToken cancellationToken)
    {
        if (!outputs.Select(item => item.PlacementId).Order().SequenceEqual(preview.Plan.RequiredSources.Order()))
            throw new InvalidDataException("The batch output set is incomplete.");
        var inventory = new List<PdfRenameDocument>();
        foreach (var source in preview.Sources)
        {
            var output = outputs.SingleOrDefault(item => item.PlacementId == source.PlacementId);
            var version = await versions.GetVersionAsync(output?.VersionId ?? source.VersionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("A final graph PDF version is missing.");
            await files.VerifyAsync(version, cancellationToken).ConfigureAwait(false);
            var href = output?.Href ?? source.Href;
            if (output is not null)
            {
                var expectedHref = preview.Plan.Changes.SingleOrDefault(item => item.PlacementId == source.PlacementId)?.NewHref ?? source.Href;
                if (href != expectedHref || version.ParentVersionId != source.VersionId || version.SourceDocumentId != source.DocumentId ||
                    version.ApplicationId != preview.Plan.ApplicationId || version.SequenceNumber != preview.Plan.SequenceNumber ||
                    version.PlanDigest != preview.Plan.PlanDigest || version.EngineVersion != preview.EngineVersion ||
                    version.Sha256 != output.Sha256 || version.Md5 != output.Md5)
                    throw new InvalidDataException("A batch output no longer matches its immutable authorization.");
                var original = await versions.GetVersionAsync(source.VersionId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("A captured source is missing.");
                await files.VerifyAsync(original, cancellationToken).ConfigureAwait(false);
                var edits = preview.Plan.Impacts.Where(item => item.SourcePlacementId == source.PlacementId)
                    .Select(item => new PdfReferenceEdit(item.SourcePageNumber!.Value, item.ObjectPath, item.OldReference, item.NewReference, item.IsUri)).ToArray();
                ValidateOutput(original, source, version.StoragePath, href, edits, cancellationToken);
            }
            inventory.Add(new(source.PlacementId, source.DocumentId, version.Id, source.SequenceNumber, href,
                version.Sha256, source.Editable, Inspect(version.StoragePath, source.SequenceNumber + "/" + href, cancellationToken)));
        }
        var validated = PdfRenamePlanner.Create(preview.Plan.ApplicationId, preview.Plan.SequenceNumber,
            preview.Plan.ExpectedRevision, inventory, [], []);
        if (validated.Issues.Count > 0) throw new PdfProcessingRejectedException("The final batch graph has unresolved or invalid PDF references.");
    }

    private async Task<PdfRenameJobView> ViewAsync(PdfRenameJob job, CancellationToken cancellationToken)
    {
        var adoption = await renameStore.GetAdoptionAsync(job.State.Id, cancellationToken).ConfigureAwait(false);
        return new(job.State.Id, job.State.PlanId, job.State.Status, job.State.Stage, job.State.AttemptCount,
            job.State.CancellationRequested, job.State.CreatedUtc, job.State.CompletedUtc, job.State.FailureCode,
            job.State.FailureMessage, job.Results, adoption);
    }
}
