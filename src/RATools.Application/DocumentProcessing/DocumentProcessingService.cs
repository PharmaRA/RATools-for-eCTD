using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Documents;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Domain.PackageValidation;

namespace RATools.Application.DocumentProcessing;

public sealed record ProcessingInspection(Guid SourceVersionId, Guid SourceDocumentId, long WorkspaceRevision,
    string Sha256, string Md5, string Href, PdfNavigationInspection Navigation);
public sealed record CreateProcessingPlanRequest(Guid SourceVersionId, long ExpectedRevision, PdfProcessingParameters Parameters);
public sealed record ProcessingPlanContext(string SourceHref, IReadOnlyList<ProcessingTargetBinding> Targets,
    PdfContentInspection? ContentInspection = null);
public sealed record ProcessingTargetBinding(string LogicalPath, Guid PlacementId, Guid VersionId, string Sha256);
public sealed record ProcessingContent(string Path, string FileName, long Length, string Sha256);
public sealed record ProcessingJobView(Guid Id, Guid PlanId, PdfProcessingJobStatus Status, string Stage, int AttemptCount,
    bool CancellationRequested, DateTime CreatedUtc, DateTime? CompletedUtc, string? FailureCode, string? FailureMessage,
    Guid? ResultVersionId, string? ResultSha256, string? ResultMd5, PdfProcessingAdoption? Adoption);

public sealed class DocumentProcessingService(
    IDocumentProcessingStore store, IApplicationRepository applications, IDocumentRepository documents,
    IDocumentPlacementRepository placements, WorkspaceMutationCoordinator mutations,
    IDocumentStorageBoundary boundary, ProcessingFileStore files, IPdfProcessingEngine engine,
    IPackagePdfInspector inspector, PdfProcessingExecutionOptions options, IPdfContentEngine? contentEngine = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const long MaxJsonBytes = 64 * 1024 * 1024;

    public async Task<PdfContentCapabilities> GetContentCapabilitiesAsync(Guid applicationId, string sequence, CancellationToken cancellationToken = default)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken).ConfigureAwait(false);
        if (application is null || application.Sequences.All(item => item.SequenceNumber != sequence))
            throw new KeyNotFoundException("The content processing workspace was not found.");
        return await RequireContentEngine().GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PdfContentInspection> InspectContentAsync(Guid applicationId, string sequence, Guid placementId,
        PdfContentInspectionRequest request, CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(applicationId, sequence, request.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        var (application, _, document) = await WorkspaceAsync(applicationId, sequence, placementId, cancellationToken).ConfigureAwait(false);
        var source = await RequireVersionAsync(request.SourceVersionId, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        await VerifyCurrentAsync(application, sequence, document, source, cancellationToken).ConfigureAwait(false);
        return await RequireContentEngine().InspectAsync(source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PdfContentEvidence> GetContentReceiptAsync(Guid applicationId, string sequence, Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var (job, plan) = await RequireJobAsync(applicationId, sequence, jobId, cancellationToken).ConfigureAwait(false);
        if (job.Status != PdfProcessingJobStatus.Completed || job.ResultVersionId is null || ReadContext(plan).ContentInspection is null)
            throw new KeyNotFoundException("This job has no completed content processing evidence.");
        var result = await RequireVersionAsync(job.ResultVersionId.Value, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        plan.VerifyResult(result); await files.VerifyAsync(result, cancellationToken).ConfigureAwait(false);
        var evidence = ReadContentEvidence(result);
        if (evidence.EngineBinding != plan.EngineVersion || evidence.Receipt.SourceSha256 != plan.SourceSha256 || evidence.Receipt.OutputSha256 != result.Sha256)
            throw new InvalidDataException("The stored content receipt is bound to a different result.");
        return evidence;
    }

    public async Task<ProcessingInspection> InspectAsync(Guid applicationId, string sequence, Guid placementId,
        long expectedRevision, CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(applicationId, sequence, expectedRevision, cancellationToken).ConfigureAwait(false);
        var (application, placement, document) = await WorkspaceAsync(applicationId, sequence, placementId, cancellationToken).ConfigureAwait(false);
        var source = await files.CaptureAsync(application, sequence, document, cancellationToken).ConfigureAwait(false);
        var navigation = await engine.InspectAsync(source.StoragePath, MaxJsonBytes, cancellationToken).ConfigureAwait(false);
        await files.VerifyAsync(source, cancellationToken).ConfigureAwait(false);
        await store.AddVersionAsync(source, cancellationToken).ConfigureAwait(false);
        return new(source.Id, document.Id, mutation.Revision, source.Sha256, source.Md5,
            PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber), navigation);
    }

    public async Task<PdfProcessingPlan> CreatePlanAsync(Guid applicationId, string sequence, Guid placementId,
        CreateProcessingPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Parameters.ReferenceEdits is { Count: > 0 })
            throw new PdfProcessingRejectedException("File and URI reference rewrites require a confirmed PDF rename batch.");
        await using var mutation = await mutations.AcquireAsync(applicationId, sequence, request.ExpectedRevision, cancellationToken).ConfigureAwait(false);
        var (application, _, document) = await WorkspaceAsync(applicationId, sequence, placementId, cancellationToken).ConfigureAwait(false);
        var source = await RequireVersionAsync(request.SourceVersionId, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        await VerifyCurrentAsync(application, sequence, document, source, cancellationToken).ConfigureAwait(false);
        var parameters = request.Parameters.Normalize();
        var navigation = await engine.InspectAsync(source.StoragePath, MaxJsonBytes, cancellationToken).ConfigureAwait(false);
        EnsureSafeNavigation(navigation);
        ValidateEdits(navigation, parameters);
        var href = PublishOutputNaming.BuildPublishedDocumentRelativePath(document, sequence);
        var targets = await CaptureTargetsAsync(application, sequence, href, navigation, parameters, cancellationToken).ConfigureAwait(false);
        PdfContentInspection? content = null;
        if (parameters.Content is not null)
        {
            content = await RequireContentEngine().InspectAsync(source, cancellationToken).ConfigureAwait(false);
            parameters.Content.ValidateAgainst(content);
            if (content.PageCount != navigation.PageCount) throw new PdfProcessingRejectedException("Independent inspectors disagree on source page count.");
        }
        var context = new ProcessingPlanContext(href, targets, content);
        var plan = PdfProcessingPlan.Create(source, applicationId, sequence, placementId, request.ExpectedRevision, parameters,
            content?.Capabilities.EngineBinding ?? engine.EngineVersion, new(navigation.IsEncrypted, navigation.HasSignature, navigation.PageCount,
                navigation.NavigationComplete, navigation.DestinationsComplete), JsonSerializer.Serialize(context, Json));
        await store.AddPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        return plan;
    }

    public async Task<PdfProcessingEnqueueResult> EnqueueAsync(Guid applicationId, string sequence, Guid planId,
        string planDigest, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var plan = await RequirePlanAsync(planId, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        if (plan.PlanDigest != planDigest) throw new PdfProcessingRejectedException("The reviewed plan digest changed.");
        var request = PdfProcessingJob.Create(plan, idempotencyKey);
        var existing = (await store.ListJobsAsync(planId, cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(job => job.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
        {
            if (existing.RequestDigest != request.RequestDigest || existing.PlanDigest != plan.PlanDigest)
                throw new PdfProcessingIdempotencyConflictException(request.IdempotencyKey);
            return new(existing, false);
        }
        await using var mutation = await mutations.AcquireAsync(applicationId, sequence, plan.ExpectedWorkspaceRevision, cancellationToken).ConfigureAwait(false);
        var (application, _, document) = await WorkspaceAsync(applicationId, sequence, plan.PlacementId, cancellationToken).ConfigureAwait(false);
        var source = await RequireVersionAsync(plan.SourceVersionId, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        await VerifyCurrentAsync(application, sequence, document, source, cancellationToken).ConfigureAwait(false);
        if (DateTime.UtcNow - plan.CreatedUtc > TimeSpan.FromHours(24))
            throw new PdfProcessingRejectedException("The processing preview expired; create a new plan.");
        if (ReadContext(plan).ContentInspection is { } content &&
            (await RequireContentEngine().GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false)).EngineBinding != content.Capabilities.EngineBinding)
            throw new PdfProcessingRejectedException("Content dependencies changed; create a new inspected plan.");
        return await store.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessingJobView> GetJobAsync(Guid applicationId, string sequence, Guid jobId, CancellationToken cancellationToken = default)
    {
        var (job, _) = await RequireJobAsync(applicationId, sequence, jobId, cancellationToken).ConfigureAwait(false);
        return View(job, await store.GetAdoptionAsync(job.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ProcessingJobView> CancelAsync(Guid applicationId, string sequence, Guid jobId, CancellationToken cancellationToken = default)
    {
        _ = await RequireJobAsync(applicationId, sequence, jobId, cancellationToken).ConfigureAwait(false);
        var job = await store.CancelAsync(jobId, DateTime.UtcNow, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Processing job was not found.");
        return View(job, await store.GetAdoptionAsync(job.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ProcessingContent> OpenVersionAsync(Guid applicationId, string sequence, Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await RequireVersionAsync(versionId, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        await files.VerifyAsync(version, cancellationToken).ConfigureAwait(false);
        return new(version.StoragePath, version.FileName, version.FileSize, version.Sha256);
    }

    public async Task ExecuteAsync(PdfProcessingLease lease, CancellationToken cancellationToken = default)
    {
        var plan = await store.GetPlanAsync(lease.Job.PlanId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Processing plan is missing.");
        plan.Verify();
        var parameters = JsonSerializer.Deserialize<PdfProcessingParameters>(plan.ParametersJson, Json)
            ?? throw new InvalidDataException("Processing parameters are missing.");
        var content = ReadContext(plan).ContentInspection;
        if (plan.PlanDigest != lease.Job.PlanDigest || plan.EngineVersion != (parameters.Content is null ? engine.EngineVersion : content?.Capabilities.EngineBinding))
            throw new InvalidDataException("Processing engine or request binding changed.");
        var application = await applications.GetAsync(plan.ApplicationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Application was not found.");
        var source = await RequireVersionAsync(plan.SourceVersionId, plan.ApplicationId, plan.SequenceNumber, cancellationToken).ConfigureAwait(false);
        await files.VerifyAsync(source, cancellationToken).ConfigureAwait(false);
        var output = files.CreateAttemptOutput(application, lease.Job.Id, lease.Token);
        PdfContentEvidence? evidence = null;
        if (parameters.Content is not null)
            evidence = await RequireContentEngine().ExecuteAsync(source, output, parameters.Content, content!, options.ExecutionTimeout,
                options.MaxFileBytes, cancellationToken).ConfigureAwait(false);
        else
        {
            var result = await engine.ExecuteAsync(new(source.StoragePath, output, parameters, options.ExecutionTimeout,
                MaxJsonBytes, options.MaxFileBytes), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded) throw new PdfProcessingRejectedException(result.ErrorMessage ?? "The PDF engine did not produce a verified result.");
        }
        await files.VerifyAsync(source, cancellationToken).ConfigureAwait(false);
        var hash = await files.MeasureAsync(output, cancellationToken).ConfigureAwait(false);
        await ValidateOutputAsync(plan, source, output, evidence, cancellationToken).ConfigureAwait(false);
        var version = DocumentVersion.CreateResult(plan.ApplicationId, plan.SequenceNumber, plan.SourceDocumentId, source.Id,
            source.FileName, source.MediaType, hash.Length, hash.Sha256, hash.Md5, output, DateTime.UtcNow,
            plan.EngineVersion, plan.ParametersJson, plan.PlanDigest, evidence is null ? "{}" : JsonSerializer.Serialize(evidence, Json));
        await files.VerifyAsync(version, cancellationToken).ConfigureAwait(false);
        if (!await store.CompleteAsync(lease.Job.Id, lease.Token, lease.Owner, DateTime.UtcNow, version, cancellationToken).ConfigureAwait(false))
            throw new PdfProcessingRejectedException("Processing lease was lost or cancellation was requested; output was not registered.");
    }

    public async Task<PdfProcessingAdoption> AdoptAsync(Guid applicationId, string sequence, Guid jobId,
        long expectedRevision, CancellationToken cancellationToken = default)
    {
        var (job, plan) = await RequireJobAsync(applicationId, sequence, jobId, cancellationToken).ConfigureAwait(false);
        var replay = await store.GetAdoptionAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return VerifyReplay(replay, expectedRevision);
        if (job.Status != PdfProcessingJobStatus.Completed || job.ResultVersionId is null)
            throw new PdfProcessingRejectedException("Only a completed and verified processing result may be adopted.");
        if (expectedRevision != plan.ExpectedWorkspaceRevision)
            throw new PdfProcessingRejectedException("Adoption must use the reviewed workspace revision.");
        WorkspaceMutation acquired;
        try
        {
            acquired = await mutations.AcquireAsync(applicationId, sequence, expectedRevision, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkspaceRevisionConflictException)
        {
            // A concurrent identical adoption may have committed while this call
            // waited for the application lock. Replay only that exact job and
            // original revision; unrelated edits remain revision conflicts.
            replay = await store.GetAdoptionAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (replay is not null) return VerifyReplay(replay, expectedRevision);
            throw;
        }
        await using var mutation = acquired;
        replay = await store.GetAdoptionAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return VerifyReplay(replay, expectedRevision);
        var (application, placement, document) = await WorkspaceAsync(applicationId, sequence, plan.PlacementId, cancellationToken).ConfigureAwait(false);
        var source = await RequireVersionAsync(plan.SourceVersionId, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        await VerifyCurrentAsync(application, sequence, document, source, cancellationToken).ConfigureAwait(false);
        var result = await RequireVersionAsync(job.ResultVersionId.Value, applicationId, sequence, cancellationToken).ConfigureAwait(false);
        plan.VerifyResult(result);
        await files.VerifyAsync(result, cancellationToken).ConfigureAwait(false);
        var context = ReadContext(plan);
        if (context.SourceHref != PublishOutputNaming.BuildPublishedDocumentRelativePath(document, sequence))
            throw new PdfProcessingRejectedException("The source delivery path changed; regenerate the processing plan.");
        await ValidateOutputAsync(plan, source, result.StoragePath, context.ContentInspection is null ? null : ReadContentEvidence(result), cancellationToken).ConfigureAwait(false);
        var draft = await files.MaterializeAsync(application, sequence, jobId, result, context.SourceHref, cancellationToken).ConfigureAwait(false);
        var replacement = DocumentPlacement.Rehydrate(placement.Id, draft.Id, placement.ApplicationId, placement.SequenceNumber,
            placement.CtdSection, placement.Operation, placement.Title, placement.LifecycleTargetPlacementId, placement.CreatedUtc,
            placement.LeafId, placement.NodeInstanceId, placement.SortOrder, placement.ImportedSource);
        var adoption = new PdfProcessingAdoption(jobId, applicationId, sequence, placement.Id, draft.Id, result.Id,
            expectedRevision, expectedRevision + 1, DateTime.UtcNow);
        await mutation.CommitAsync(async ct =>
        {
            await documents.AddAsync(draft, ct).ConfigureAwait(false);
            if (!await placements.UpdateAsync(replacement, ct).ConfigureAwait(false))
                throw new PdfProcessingRejectedException("The selected placement no longer exists.");
            await store.AddAdoptionAsync(adoption, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return adoption;
    }

    private async Task ValidateOutputAsync(PdfProcessingPlan plan, DocumentVersion source, string output,
        PdfContentEvidence? contentEvidence, CancellationToken cancellationToken)
    {
        var context = ReadContext(plan);
        var original = InspectPackage(source.StoragePath, plan.SequenceNumber + "/" + context.SourceHref, cancellationToken);
        var processed = InspectPackage(output, plan.SequenceNumber + "/" + context.SourceHref, cancellationToken);
        if (context.ContentInspection is { } content)
        {
            var parameters = JsonSerializer.Deserialize<PdfProcessingParameters>(plan.ParametersJson, Json)?.Content
                ?? throw new InvalidDataException("Content processing parameters are missing.");
            await RequireContentEngine().VerifyEvidenceAsync(source, output, parameters, content,
                contentEvidence ?? throw new InvalidDataException("Content processing evidence is missing."), cancellationToken).ConfigureAwait(false);
            if (processed.Basics.PageCount != original.Basics.PageCount || !original.Links.SequenceEqual(processed.Links) ||
                !original.Destinations.SequenceEqual(processed.Destinations) ||
                (original.Basics.HasSearchableText && !processed.Basics.HasSearchableText) ||
                (parameters.Ocr is null && original.Basics.HasSearchableText != processed.Basics.HasSearchableText) ||
                (contentEvidence!.Receipt.Ocr?.ProcessedPages.Count > 0 && !processed.Basics.HasSearchableText) ||
                (parameters.EmbedStandardFonts && (processed.Basics.AllFontsEmbedded != true || processed.Basics.NonEmbeddedFonts.Count > 0)) ||
                (!parameters.EmbedStandardFonts && !original.Basics.NonEmbeddedFonts.Order(StringComparer.Ordinal).SequenceEqual(processed.Basics.NonEmbeddedFonts.Order(StringComparer.Ordinal))) ||
                (parameters.TargetVersion is not null && processed.Basics.PdfVersion != parameters.TargetVersion))
                throw new PdfProcessingRejectedException("Independent P2 facts do not confirm the requested content operation or unchanged navigation.");
            var before = await engine.InspectAsync(source.StoragePath, MaxJsonBytes, cancellationToken).ConfigureAwait(false);
            var after = await engine.InspectAsync(output, MaxJsonBytes, cancellationToken).ConfigureAwait(false);
            EnsureSafeNavigation(after);
            if (before.PageCount != after.PageCount || before.PageMode != after.PageMode || !before.Bookmarks.SequenceEqual(after.Bookmarks) ||
                !before.Links.SequenceEqual(after.Links) || !before.NamedDestinations.SequenceEqual(after.NamedDestinations))
                throw new PdfProcessingRejectedException("The content operation changed unselected PDF navigation.");
        }
        else if (processed.Basics.PageCount != original.Basics.PageCount || processed.Basics.HasSearchableText != original.Basics.HasSearchableText ||
            processed.Basics.AllFontsEmbedded != original.Basics.AllFontsEmbedded ||
            !processed.Basics.NonEmbeddedFonts.Order(StringComparer.Ordinal).SequenceEqual(original.Basics.NonEmbeddedFonts.Order(StringComparer.Ordinal)))
            throw new PdfProcessingRejectedException("Page count, searchable text or font facts changed during navigation processing.");
        var known = new Dictionary<string, PackagePdfFacts>(StringComparer.Ordinal)
        { [plan.SequenceNumber + "/" + context.SourceHref] = processed };
        foreach (var binding in context.Targets)
        {
            var target = await store.GetVersionAsync(binding.VersionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("A captured link target is missing.");
            if (target.ApplicationId != plan.ApplicationId || target.Sha256 != binding.Sha256)
                throw new InvalidDataException("Captured target identity changed.");
            await files.VerifyAsync(target, cancellationToken).ConfigureAwait(false);
            // Current delivery target must still have the same bytes and href; a
            // frozen target does not authorize a link to a changed working file.
            var placement = await placements.GetAsync(binding.PlacementId, cancellationToken).ConfigureAwait(false)
                ?? throw new PdfProcessingRejectedException("A link target placement was removed.");
            var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false)
                ?? throw new PdfProcessingRejectedException("A link target document was removed.");
            if (placement.ApplicationId != plan.ApplicationId || placement.Operation == DocumentPlacementOperation.Delete ||
                binding.LogicalPath != placement.SequenceNumber + "/" + PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber) ||
                document.Sha256 != target.Sha256)
                throw new PdfProcessingRejectedException("A delivery link target changed; regenerate the processing plan.");
            var measured = await files.MeasureAsync(document.StoragePath, cancellationToken).ConfigureAwait(false);
            if (measured.Sha256 != target.Sha256) throw new PdfProcessingRejectedException("A delivery link target changed on disk.");
            known[binding.LogicalPath] = InspectPackage(target.StoragePath, binding.LogicalPath, cancellationToken);
        }
        foreach (var link in processed.Links)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = link.Target;
            if (target.Invalid || target.UnsupportedReason is not null) throw new PdfProcessingRejectedException("An output PDF action is invalid or unverified.");
            var facts = processed;
            if (target.FileSpecification is not null)
            {
                var logical = ResolveFile(plan.SequenceNumber + "/" + context.SourceHref, target.FileSpecification);
                if (!known.TryGetValue(logical, out facts)) throw new PdfProcessingRejectedException("An output remote PDF target was not captured by this plan.");
            }
            else if (target.Uri is not null)
            {
                var reference = PackageLogicalPath.ResolveReference(plan.SequenceNumber + "/" + context.SourceHref, target.Uri);
                if (reference.ExternalUri is not null) continue;
                if (reference.LogicalPath is null || !known.TryGetValue(reference.LogicalPath, out facts))
                    throw new PdfProcessingRejectedException("A relative PDF URI target was not captured by this plan.");
                if (reference.Fragment is { } fragment)
                {
                    var destination = ParseRemoteDestination("target.pdf#" + fragment);
                    target = target with { PageNumber = destination.Page, NamedDestination = destination.Named };
                }
            }
            if (target.PageNumber is { } page && (page < 1 || page > facts.Basics.PageCount))
                throw new PdfProcessingRejectedException("An output link page is outside its target document.");
            if (target.NamedDestination is { } named && !facts.Destinations.Any(item => item.Name == named && !item.Invalid && item.PageNumber is >= 1 && item.PageNumber <= facts.Basics.PageCount))
                throw new PdfProcessingRejectedException("An output named destination does not resolve in its target document.");
        }
    }

    private async Task<IReadOnlyList<ProcessingTargetBinding>> CaptureTargetsAsync(SubmissionApplication application, string sequence,
        string href, PdfNavigationInspection navigation, PdfProcessingParameters parameters, CancellationToken cancellationToken)
    {
        var edits = (parameters.Links ?? []).ToDictionary(edit => edit.ObjectPath!, StringComparer.Ordinal);
        var requests = new List<(string LogicalPath, Guid? PlacementId, Guid? VersionId, int? Page, string? Named)>();
        foreach (var link in navigation.Links)
        {
            var changed = edits.GetValueOrDefault(link.ObjectPath);
            var kind = changed?.TargetKind ?? link.Kind;
            if (kind == PdfLinkTargetKind.ExternalUri && changed is null)
            {
                var reference = PackageLogicalPath.ResolveReference(sequence + "/" + href, link.Target);
                if (reference.ExternalUri is not null) continue;
                if (reference.LogicalPath is null) throw new PdfProcessingRejectedException("A relative URI target could not be resolved.");
                var uriDestination = reference.Fragment is null ? ((int?)null, (string?)null) : ParseRemoteDestination("target.pdf#" + reference.Fragment);
                requests.Add((reference.LogicalPath, null, null, uriDestination.Item1, uriDestination.Item2));
                continue;
            }
            if (kind != PdfLinkTargetKind.RelativeRemotePdf) continue;
            var file = changed is null ? link.FileSpecification : changed.Target.Split('#', 2)[0];
            if (file is null) throw new PdfProcessingRejectedException("Remote PDF link has no resolvable file specification.");
            var destination = changed is null ? (link.TargetPageNumber, link.NamedDestination) : ParseRemoteDestination(changed.Target);
            requests.Add((ResolveFile(sequence + "/" + href, file), changed?.TargetPlacementId, changed?.TargetVersionId,
                destination.Item1, destination.Item2));
        }
        var all = await placements.ListByApplicationAsync(application.Id, cancellationToken).ConfigureAwait(false);
        var bindings = new List<ProcessingTargetBinding>();
        foreach (var request in requests.Distinct())
        {
            if (request.LogicalPath == sequence + "/" + href)
            {
                ValidateDestination(navigation.PageCount, navigation.NamedDestinations.Where(item => item.IsValid).Select(item => item.Name), request.Page, request.Named);
                continue;
            }
            var matches = new List<(DocumentPlacement Placement, SubmissionDocument Document)>();
            foreach (var candidate in all.Where(item => item.Operation != DocumentPlacementOperation.Delete && string.CompareOrdinal(item.SequenceNumber, sequence) <= 0))
            {
                if (request.PlacementId is not null && candidate.Id != request.PlacementId) continue;
                var doc = await documents.GetAsync(candidate.DocumentId, cancellationToken).ConfigureAwait(false);
                if (doc is not null && candidate.SequenceNumber + "/" + PublishOutputNaming.BuildPublishedDocumentRelativePath(doc, candidate.SequenceNumber) == request.LogicalPath)
                    matches.Add((candidate, doc));
            }
            if (matches.Count != 1) throw new PdfProcessingRejectedException("Select an unambiguous existing PDF target in this application.");
            var match = matches[0];
            DocumentVersion target;
            if (request.VersionId is { } versionId)
            {
                target = await RequireVersionAsync(versionId, application.Id, match.Placement.SequenceNumber, cancellationToken).ConfigureAwait(false);
                await VerifyCurrentAsync(application, match.Placement.SequenceNumber, match.Document, target, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                target = await files.CaptureAsync(application, match.Placement.SequenceNumber, match.Document, cancellationToken).ConfigureAwait(false);
                await store.AddVersionAsync(target, cancellationToken).ConfigureAwait(false);
            }
            var facts = InspectPackage(target.StoragePath, request.LogicalPath, cancellationToken);
            ValidateDestination(facts.Basics.PageCount, facts.Destinations.Where(item => !item.Invalid && item.PageNumber is >= 1).Select(item => item.Name),
                request.Page, request.Named);
            bindings.Add(new(request.LogicalPath, match.Placement.Id, target.Id, target.Sha256));
        }
        return bindings.OrderBy(item => item.LogicalPath, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateEdits(PdfNavigationInspection navigation, PdfProcessingParameters parameters)
    {
        var namedTargets = navigation.NamedDestinations.Where(item => item.IsValid && item.PageNumber is >= 1).Select(item => item.Name).ToArray();
        var priorLevel = -1;
        foreach (var bookmark in parameters.Bookmarks ?? [])
        {
            if (bookmark.Level > priorLevel + 1) throw new PdfProcessingRejectedException("Bookmark hierarchy cannot skip a parent level.");
            priorLevel = bookmark.Level;
            ValidateDestination(navigation.PageCount, namedTargets, bookmark.PageNumber, bookmark.NamedDestination);
        }
        foreach (var edit in parameters.Links ?? [])
        {
            var old = navigation.Links.SingleOrDefault(link => link.ObjectPath == edit.ObjectPath && link.SourcePageNumber == edit.SourcePageNumber)
                ?? throw new PdfProcessingRejectedException("The selected PDF link no longer exists.");
            if (edit.OriginalTarget is null || old.Target != edit.OriginalTarget)
                throw new PdfProcessingRejectedException("The link's original target must match the reviewed inspection.");
            if (edit.TargetKind == PdfLinkTargetKind.Page)
            {
                if (!int.TryParse(edit.Target, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page))
                    throw new PdfProcessingRejectedException("A link page must be a positive integer.");
                ValidateDestination(navigation.PageCount, namedTargets, page, null);
            }
            else if (edit.TargetKind == PdfLinkTargetKind.NamedDestination)
                ValidateDestination(navigation.PageCount, namedTargets, null, edit.Target.TrimStart('#'));
            else if (edit.TargetKind == PdfLinkTargetKind.ExternalUri &&
                     (!Uri.TryCreate(edit.Target, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "mailto")))
                throw new PdfProcessingRejectedException("External navigation requires an HTTP, HTTPS or mailto URI.");
            else if (!Enum.IsDefined(edit.TargetKind)) throw new PdfProcessingRejectedException("Unsupported link target type.");
        }
        if ((parameters.Links ?? []).GroupBy(edit => edit.ObjectPath).Any(group => group.Count() > 1))
            throw new PdfProcessingRejectedException("Each PDF link may be edited only once per plan.");
    }

    private static (int? Page, string? Named) ParseRemoteDestination(string target)
    {
        var parsed = PdfRemoteTarget.Parse(target);
        return (parsed.PageNumber, parsed.NamedDestination);
    }

    private static void ValidateDestination(int? pageCount, IEnumerable<string> names, int? page, string? named)
    {
        if (page is { } number && (number < 1 || pageCount is null || number > pageCount))
            throw new PdfProcessingRejectedException("The selected page is outside the target PDF.");
        if (named is not null && !names.Contains(named, StringComparer.Ordinal))
            throw new PdfProcessingRejectedException("The selected named destination does not exist in the target PDF.");
    }

    private PackagePdfFacts InspectPackage(string path, string logical, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var facts = inspector.Inspect(stream, logical, new PackageReadLimits { MaxFileBytes = options.MaxFileBytes }, cancellationToken);
        if (facts.Basics.ParseError is not null || facts.Basics.IsEncrypted || !facts.NavigationComplete || !facts.DestinationsComplete || facts.Basics.PageCount is not > 0)
            throw new PdfProcessingRejectedException("PDF inspection is incomplete or invalid; the result cannot be adopted.");
        return facts;
    }

    private static void EnsureSafeNavigation(PdfNavigationInspection navigation)
    {
        if (navigation.IsEncrypted || navigation.HasSignature || !navigation.NavigationComplete || !navigation.DestinationsComplete || navigation.PageCount is not > 0)
            throw new PdfProcessingRejectedException("Encrypted, signed or incompletely inspected PDFs cannot be processed.");
    }

    private async Task VerifyCurrentAsync(SubmissionApplication application, string sequence, SubmissionDocument document,
        DocumentVersion source, CancellationToken cancellationToken)
    {
        if (source.SourceDocumentId != document.Id || source.Sha256 != document.Sha256 || source.Md5 != document.Md5 || source.FileSize != document.FileSize)
            throw new PdfProcessingRejectedException("The source document version changed; refresh the preview.");
        var path = boundary.EnsureDocumentOwnedBySequence(document, application, sequence);
        var measured = await files.MeasureAsync(path, cancellationToken).ConfigureAwait(false);
        if (measured.Sha256 != source.Sha256 || measured.Md5 != source.Md5 || measured.Length != source.FileSize)
            throw new PdfProcessingRejectedException("Source bytes changed since the preview.");
        await files.VerifyAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(SubmissionApplication, DocumentPlacement, SubmissionDocument)> WorkspaceAsync(Guid applicationId, string sequence, Guid placementId, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Application was not found.");
        var placement = await placements.GetAsync(placementId, cancellationToken).ConfigureAwait(false);
        if (placement is null || placement.ApplicationId != applicationId || placement.SequenceNumber != sequence)
            throw new KeyNotFoundException("Placement was not found in this workspace.");
        if (placement.Operation == DocumentPlacementOperation.Delete) throw new PdfProcessingRejectedException("Delete instructions have no editable PDF payload.");
        var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Source document was not found.");
        if (document.MediaType != "application/pdf") throw new PdfProcessingRejectedException("Only PDF content can be processed.");
        return (application, placement, document);
    }

    private async Task<DocumentVersion> RequireVersionAsync(Guid id, Guid applicationId, string sequence, CancellationToken cancellationToken)
    {
        var version = await store.GetVersionAsync(id, cancellationToken).ConfigureAwait(false);
        if (version is null || version.ApplicationId != applicationId || version.SequenceNumber != sequence)
            throw new KeyNotFoundException("Document version was not found in this workspace.");
        return version;
    }

    private async Task<PdfProcessingPlan> RequirePlanAsync(Guid id, Guid applicationId, string sequence, CancellationToken cancellationToken)
    {
        var plan = await store.GetPlanAsync(id, cancellationToken).ConfigureAwait(false);
        if (plan is null || plan.ApplicationId != applicationId || plan.SequenceNumber != sequence)
            throw new KeyNotFoundException("Processing plan was not found in this workspace.");
        plan.Verify(); return plan;
    }

    private async Task<(PdfProcessingJob, PdfProcessingPlan)> RequireJobAsync(Guid applicationId, string sequence, Guid id, CancellationToken cancellationToken)
    {
        var job = await store.GetJobAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Processing job was not found.");
        return (job, await RequirePlanAsync(job.PlanId, applicationId, sequence, cancellationToken).ConfigureAwait(false));
    }

    private static string ResolveFile(string source, string file)
    {
        if (file.Contains('\\') || file.StartsWith('/') || file.Contains(':')) throw new PdfProcessingRejectedException("Remote target must remain inside the application.");
        var encoded = string.Join('/', file.Split('/').Select(segment => segment is "." or ".." ? segment : Uri.EscapeDataString(segment)));
        return PackageLogicalPath.ResolveReference(source, encoded).LogicalPath
            ?? throw new PdfProcessingRejectedException("Remote target must resolve to an application file.");
    }

    private static ProcessingPlanContext ReadContext(PdfProcessingPlan plan) => JsonSerializer.Deserialize<ProcessingPlanContext>(plan.ContextJson, Json)
        ?? throw new InvalidDataException("Processing plan context is missing.");
    private IPdfContentEngine RequireContentEngine() => contentEngine
        ?? throw new PdfProcessingRejectedException("PDF content processing is not configured.");
    private static PdfContentEvidence ReadContentEvidence(DocumentVersion version)
    {
        var evidence = JsonSerializer.Deserialize<PdfContentEvidence>(version.EvidenceJson, Json);
        if (evidence?.Receipt is null || evidence.Resources is null || string.IsNullOrWhiteSpace(evidence.EngineBinding))
            throw new InvalidDataException("Immutable content processing evidence is missing.");
        return evidence;
    }
    private static PdfProcessingAdoption VerifyReplay(PdfProcessingAdoption adoption, long revision) => adoption.ExpectedRevision == revision
        ? adoption : throw new PdfProcessingRejectedException("An adoption retry must use its original reviewed revision.");
    private static ProcessingJobView View(PdfProcessingJob job, PdfProcessingAdoption? adoption) => new(job.Id, job.PlanId, job.Status,
        job.Stage, job.AttemptCount, job.CancellationRequested, job.CreatedUtc, job.CompletedUtc, job.FailureCode,
        job.FailureMessage, job.ResultVersionId, job.ResultSha256, job.ResultMd5, adoption);
}
