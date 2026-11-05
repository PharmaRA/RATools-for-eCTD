using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing;
using RATools.Application.Workspaces;
using RATools.Domain.Documents;
using RATools.Domain.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RATools.Application.DocumentProcessing;

public sealed record PdfRelocationChange(Guid PlacementId, string FileName, Guid? TargetNodeInstanceId = null);
public sealed record PdfRenameSource(Guid PlacementId, Guid DocumentId, Guid VersionId, string SequenceNumber,
    string Href, string Sha256, bool Editable);
public sealed record PdfRenamePreview(Guid Id, PdfRenamePlan Plan, IReadOnlyList<PdfRenameSource> Sources,
    IReadOnlyList<PdfRelocationChange> Changes, string EngineVersion, DateTime CreatedUtc, DateTime ExpiresUtc,
    IReadOnlyList<PdfRenamePlacementBinding> Inventory)
{
    public string ComputeDigest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        Schema = "pdf-rename-preview-v1", Id, Plan = Plan with { PlanDigest = "" }, Sources, Changes,
        EngineVersion, CreatedUtc, ExpiresUtc, Inventory
    })))).ToLowerInvariant();

    public void Verify()
    {
        if (Plan.PlanDigest != ComputeDigest() || ExpiresUtc <= CreatedUtc || Id == Guid.Empty)
            throw new InvalidDataException("Stored PDF rename preview evidence failed verification.");
    }
}

public sealed class PdfRenamePreviewService(IApplicationRepository applications, IDocumentRepository documents,
    IDocumentPlacementRepository placements, ICtdNodeRepository nodes, CtdNodePathResolver paths,
    WorkspaceMutationCoordinator mutations, ProcessingFileStore files, IDocumentProcessingStore versions,
    IPdfProcessingEngine engine, IPackagePdfInspector inspector, PdfProcessingExecutionOptions options)
{
    public async Task<PdfRenamePreview> PreviewAsync(Guid applicationId, string sequence, long expectedRevision,
        IReadOnlyList<PdfRelocationChange> changes, IReadOnlyList<Guid> confirmedSources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(confirmedSources);
        if (changes.Count is < 1 or > 1000 || changes.Select(item => item.PlacementId).Distinct().Count() != changes.Count)
            throw new ArgumentException("Choose between one and 1000 distinct placements to relocate.", nameof(changes));
        await using var mutation = await mutations.AcquireAsync(applicationId, sequence, expectedRevision, cancellationToken).ConfigureAwait(false);
        var application = await applications.GetAsync(applicationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The requested application was not found.");
        var all = (await placements.ListByApplicationAsync(applicationId, cancellationToken).ConfigureAwait(false))
            .Where(item => item.ApplicationId == applicationId && item.Operation != DocumentPlacementOperation.Delete)
            .OrderBy(item => item.Id).ToArray();
        if (all.Length > 10000) throw new PdfProcessingRejectedException("The application exceeds the rename inventory limit.");
        var byId = all.ToDictionary(item => item.Id);
        var loaded = new Dictionary<Guid, SubmissionDocument>();
        foreach (var change in changes)
        {
            if (!byId.TryGetValue(change.PlacementId, out var placement) || placement.SequenceNumber != sequence)
                throw new KeyNotFoundException("The relocation placement does not belong to this draft.");
            ValidateFileName(change.FileName);
            var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The relocation document was not found.");
            if (document.MediaType != "application/pdf") throw new PdfProcessingRejectedException("PDF relocation requires a PDF source.");
            loaded[document.Id] = document;
        }
        var workspace = changes.Any(item => item.TargetNodeInstanceId is not null)
            ? await nodes.GetSequenceAsync(applicationId, sequence, cancellationToken).ConfigureAwait(false) : null;
        var requests = new List<PdfRenameRequest>();
        foreach (var change in changes)
        {
            var placement = byId[change.PlacementId];
            var document = loaded[placement.DocumentId];
            string newHref;
            if (change.TargetNodeInstanceId is { } targetId)
            {
                var node = workspace?.Nodes.SingleOrDefault(item => item.NodeInstanceId == targetId)
                    ?? throw new KeyNotFoundException("The target business node is absent from this draft.");
                if (!node.AllowsLeaves) throw new PdfProcessingRejectedException("The selected business node cannot receive PDF leaves.");
                if (placement.Operation != DocumentPlacementOperation.New && placement.NodeInstanceId != targetId)
                    throw new PdfProcessingRejectedException("A lifecycle operation cannot move away from its historical business context.");
                if (placement.NodeInstanceId != targetId && all.Any(item => item.LifecycleTargetPlacementId == placement.Id))
                    throw new PdfProcessingRejectedException("A referenced historical leaf cannot move to a different business context.");
                newHref = paths.ResolveFile(application.EctdTemplateKey, workspace!, targetId, change.FileName);
            }
            else
            {
                var old = PublishOutputNaming.BuildPublishedDocumentRelativePath(document, sequence);
                var separator = old.LastIndexOf('/');
                if (separator < 0) throw new PdfProcessingRejectedException("The current document has no valid delivery directory.");
                newHref = old[..(separator + 1)] + change.FileName;
            }
            requests.Add(new(placement.Id, newHref, change.TargetNodeInstanceId));
        }
        var totalBytes = 0L;
        foreach (var placement in all)
        {
            if (!loaded.TryGetValue(placement.DocumentId, out var document))
            {
                document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("An inventory document is missing.");
                loaded[document.Id] = document;
            }
            if (document.MediaType != "application/pdf") continue;
            totalBytes = checked(totalBytes + document.FileSize);
            if (totalBytes > options.MaxFileBytes) throw new PdfProcessingRejectedException("The PDF rename inventory exceeds its total byte limit.");
        }
        var inventory = new List<PdfRenameDocument>();
        var sources = new List<PdfRenameSource>();
        foreach (var placement in all)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = loaded[placement.DocumentId];
            if (document.MediaType != "application/pdf") continue;
            var source = await files.CaptureAsync(application, placement.SequenceNumber, document, cancellationToken).ConfigureAwait(false);
            var href = PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber);
            var navigation = await engine.InspectAsync(source.StoragePath, 64 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            using var stream = File.OpenRead(source.StoragePath);
            var facts = inspector.Inspect(stream, placement.SequenceNumber + "/" + href,
                new PackageReadLimits { MaxFileBytes = options.MaxFileBytes }, cancellationToken);
            await files.VerifyAsync(source, cancellationToken).ConfigureAwait(false);
            await versions.AddVersionAsync(source, cancellationToken).ConfigureAwait(false);
            var editable = placement.SequenceNumber == sequence && !navigation.IsEncrypted && !navigation.HasSignature;
            if (!navigation.NavigationComplete || !navigation.DestinationsComplete)
                facts = new(facts.Basics, facts.Links, facts.Destinations, false, false, navigation.IncompleteReasons);
            inventory.Add(new(placement.Id, document.Id, source.Id, placement.SequenceNumber, href, source.Sha256, editable, facts));
            sources.Add(new(placement.Id, document.Id, source.Id, placement.SequenceNumber, href, source.Sha256, editable));
        }
        var plan = PdfRenamePlanner.Create(applicationId, sequence, mutation.Revision, inventory, requests, confirmedSources);
        var bindings = all.Select(placement => Bind(placement, loaded[placement.DocumentId])).ToArray();
        var finalPaths = bindings.Select(binding => binding.SequenceNumber + "/" +
            (requests.SingleOrDefault(request => request.PlacementId == binding.PlacementId)?.NewHref ?? binding.Href));
        if (finalPaths.GroupBy(path => PackageLogicalPath.CollisionKey(path)).Any(group => group.Count() > 1))
            plan = plan with { Issues = plan.Issues.Append(new PdfRenameIssue("PATH_COLLISION", null, null,
                "A final PDF path collides with another captured delivery file.")).ToArray() };
        var now = DateTime.UtcNow;
        var preview = new PdfRenamePreview(Guid.NewGuid(), plan, sources.AsReadOnly(),
            Array.AsReadOnly(changes.OrderBy(item => item.PlacementId).ToArray()), engine.EngineVersion, now, now.AddHours(24), bindings);
        return preview with { Plan = plan with { PlanDigest = preview.ComputeDigest() } };
    }

    internal static PdfRenamePlacementBinding Bind(DocumentPlacement placement, SubmissionDocument document) =>
        new(placement.Id, document.Id, placement.SequenceNumber,
            PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber), document.Sha256,
            document.Md5, document.FileSize, document.MediaType, placement.Operation, placement.NodeInstanceId,
            placement.CtdSection, placement.SortOrder, placement.LeafId, placement.LifecycleTargetPlacementId, placement.Title);

    private static void ValidateFileName(string name)
    {
        _ = PortablePathSegment.NormalizeAndValidate(name, nameof(name));
        if (name.Length > 64 || !name.EndsWith(".pdf", StringComparison.Ordinal) ||
            !char.IsAsciiLetterOrDigit(name[0]) || name.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not ('-' or '.')))
            throw new ArgumentException("Use a lowercase PDF filename of at most 64 characters without directory components.", nameof(name));
    }
}
