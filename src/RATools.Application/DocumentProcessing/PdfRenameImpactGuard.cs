using RATools.Application.Abstractions.Persistence;
using RATools.Application.Documents;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing;
using RATools.Domain.Documents;

namespace RATools.Application.DocumentProcessing;

public sealed class PdfRenameImpactGuard(IApplicationRepository applications, IDocumentRepository documents,
    IDocumentPlacementRepository placements, IDocumentStorageBoundary boundary, IPackagePdfInspector inspector,
    PdfProcessingExecutionOptions options)
{
    public async Task EnsureNoImpactAsync(DocumentPlacement changed, string finalHref, CancellationToken cancellationToken = default)
    {
        var application = await applications.GetAsync(changed.ApplicationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Application was not found.");
        var inventory = (await placements.ListByApplicationAsync(changed.ApplicationId, cancellationToken).ConfigureAwait(false))
            .Where(item => item.Operation != DocumentPlacementOperation.Delete).OrderBy(item => item.Id).ToArray();
        if (inventory.Length > 10000) throw Rejected();
        var loaded = new Dictionary<Guid, SubmissionDocument>();
        var logical = new Dictionary<Guid, string>();
        foreach (var placement in inventory)
        {
            var document = await documents.GetAsync(placement.DocumentId, cancellationToken).ConfigureAwait(false)
                ?? throw Rejected();
            loaded[placement.Id] = document;
            logical[placement.Id] = placement.SequenceNumber + "/" + PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber);
        }
        if (!logical.TryGetValue(changed.Id, out var oldPath)) throw Rejected();
        var newPath = PackageLogicalPath.Validate(changed.SequenceNumber + "/" + finalHref);
        if (oldPath == newPath) return;
        if (logical.Where(item => item.Key != changed.Id).Any(item => PackageLogicalPath.CollisionKey(item.Value) == PackageLogicalPath.CollisionKey(newPath)))
            throw Rejected();
        var targets = logical.Values.GroupBy(item => item, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        long bytes = 0;
        foreach (var placement in inventory)
        {
            var document = loaded[placement.Id];
            if (document.MediaType != "application/pdf") continue;
            var path = boundary.EnsureDocumentOwnedBySequence(document, application, placement.SequenceNumber);
            using var stream = File.OpenRead(path);
            bytes = checked(bytes + stream.Length);
            if (bytes > options.MaxFileBytes) throw Rejected();
            var facts = inspector.Inspect(stream, logical[placement.Id], new PackageReadLimits { MaxFileBytes = options.MaxFileBytes }, cancellationToken);
            if (facts.Basics.ParseError is not null || !facts.NavigationComplete || !facts.DestinationsComplete) throw Rejected();
            foreach (var link in facts.Links)
            {
                if (link.Target.Invalid || link.Target.UnsupportedReason is not null) throw Rejected();
                var reference = link.Target.FileSpecification ?? link.Target.Uri;
                if (reference is null) continue;
                if (link.Target.FileSpecification is not null)
                    reference = string.Join('/', reference.Split('/').Select(part => part is "." or ".." ? part : Uri.EscapeDataString(part)));
                PackageReference before;
                PackageReference after;
                try
                {
                    before = PackageLogicalPath.ResolveReference(logical[placement.Id], reference);
                    after = PackageLogicalPath.ResolveReference(placement.Id == changed.Id ? newPath : logical[placement.Id], reference);
                }
                catch (ArgumentException) { throw Rejected(); }
                if (before.ExternalUri is not null) continue;
                if (before.LogicalPath is null || !targets.TryGetValue(before.LogicalPath, out var count) || count != 1) throw Rejected();
                var intended = before.LogicalPath == oldPath ? newPath : before.LogicalPath;
                if (after.LogicalPath != intended) throw Rejected();
            }
        }
    }

    private static PdfProcessingRejectedException Rejected() =>
        new("This rename or move affects PDF references, or its impact cannot be fully verified. Create and confirm a PDF rename batch preview.");
}
