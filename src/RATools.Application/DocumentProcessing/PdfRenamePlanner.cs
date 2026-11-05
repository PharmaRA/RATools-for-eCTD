using RATools.Application.PackageValidation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RATools.Application.DocumentProcessing;

public sealed record PdfRenameDocument(Guid PlacementId, Guid DocumentId, Guid VersionId, string SequenceNumber,
    string Href, string Sha256, bool Editable, PackagePdfFacts Facts);
public sealed record PdfRenameRequest(Guid PlacementId, string NewHref, Guid? TargetNodeInstanceId = null);
public sealed record PdfRenameImpact(Guid SourcePlacementId, Guid TargetPlacementId, string ObjectPath,
    int? SourcePageNumber, string OldReference, string NewReference, bool IsUri, int? TargetPageNumber,
    string? NamedDestination);
public sealed record PdfRenameIssue(string Code, Guid? PlacementId, string? ObjectPath, string Message);
public sealed record PdfRenamePlan(Guid ApplicationId, string SequenceNumber, long ExpectedRevision,
    IReadOnlyList<PdfRenameRequest> Changes, IReadOnlyList<Guid> ConfirmedSources,
    IReadOnlyList<Guid> RequiredSources, IReadOnlyList<PdfRenameImpact> Impacts,
    IReadOnlyList<PdfRenameIssue> Issues, string PlanDigest)
{
    public bool CanExecute => Issues.Count == 0 && Changes.Count > 0 && RequiredSources.Count > 0;
}

public static class PdfRenamePlanner
{
    public static PdfRenamePlan Create(Guid applicationId, string sequence, long revision,
        IEnumerable<PdfRenameDocument> documents, IEnumerable<PdfRenameRequest> changes, IEnumerable<Guid> confirmedSources)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sequence);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        var inventory = documents.OrderBy(item => item.PlacementId).ToArray();
        var requested = changes.OrderBy(item => item.PlacementId).ToArray();
        var confirmed = confirmedSources.Distinct().Order().ToArray();
        if (inventory.Length > 10000 || requested.Length > 1000) throw new ArgumentException("The rename plan exceeds its document limit.");
        if (inventory.Select(item => item.PlacementId).Distinct().Count() != inventory.Length)
            throw new ArgumentException("The inventory contains repeated placement identities.", nameof(documents));
        if (requested.Select(item => item.PlacementId).Distinct().Count() != requested.Length)
            throw new ArgumentException("Each placement may be renamed only once.", nameof(changes));
        var byId = inventory.ToDictionary(item => item.PlacementId);
        var oldPaths = new Dictionary<Guid, string>();
        var finalPaths = new Dictionary<Guid, string>();
        var issues = new List<PdfRenameIssue>();
        var required = new HashSet<Guid>();
        var impacts = new List<PdfRenameImpact>();
        foreach (var document in inventory)
        {
            if (document.PlacementId == Guid.Empty || document.DocumentId == Guid.Empty || document.VersionId == Guid.Empty ||
                document.Sha256.Length != 64 || document.Sha256.Any(value => !Uri.IsHexDigit(value)))
                throw new ArgumentException("Every inventory item requires immutable content identities and a SHA-256 digest.", nameof(documents));
            oldPaths[document.PlacementId] = PackageLogicalPath.Validate(document.SequenceNumber + "/" + document.Href);
            finalPaths[document.PlacementId] = oldPaths[document.PlacementId];
            if (!document.Facts.NavigationComplete || !document.Facts.DestinationsComplete || document.Facts.Basics.ParseError is not null)
                issues.Add(new("INSPECTION_INCOMPLETE", document.PlacementId, null, "All potential referencing PDFs must have a complete inspection."));
        }
        foreach (var change in requested)
        {
            if (!byId.TryGetValue(change.PlacementId, out var document))
            { issues.Add(new("PLACEMENT_NOT_FOUND", change.PlacementId, null, "The requested placement is not in the captured inventory.")); continue; }
            try
            {
                PackageLogicalPath.Validate(change.NewHref, 230);
                if (!change.NewHref.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || !change.NewHref.Contains('/'))
                    throw new ArgumentException("A PDF delivery path inside a module is required.");
                finalPaths[document.PlacementId] = document.SequenceNumber + "/" + change.NewHref;
                if (oldPaths[document.PlacementId] != finalPaths[document.PlacementId] || change.TargetNodeInstanceId is not null)
                    required.Add(document.PlacementId);
            }
            catch (ArgumentException)
            { issues.Add(new("INVALID_PATH", document.PlacementId, null, "The new PDF delivery path is invalid.")); }
        }
        foreach (var group in finalPaths.GroupBy(item => PackageLogicalPath.CollisionKey(item.Value)).Where(group => group.Count() > 1))
            foreach (var item in group) issues.Add(new("PATH_COLLISION", item.Key, null, "Final delivery paths collide on a supported filesystem."));
        var oldGroups = oldPaths.GroupBy(item => item.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Key).ToArray(), StringComparer.Ordinal);
        foreach (var document in inventory)
        foreach (var link in document.Facts.Links.OrderBy(item => item.ObjectPath, StringComparer.Ordinal))
        {
            var target = link.Target;
            if (target.Invalid || target.UnsupportedReason is not null)
            { issues.Add(new("UNVERIFIED_LINK", document.PlacementId, link.ObjectPath, "A link is invalid or its action was not fully evaluated.")); continue; }
            var isUri = target.FileSpecification is null && target.Uri is not null;
            var reference = target.FileSpecification ?? target.Uri;
            if (reference is null)
            {
                ValidateDestination(target, document, document.PlacementId, link.ObjectPath, issues);
                continue;
            }
            PackageReference resolved;
            try
            {
                // PDF file specifications are literal filenames; URI actions are decoded exactly once.
                var address = isUri ? reference : string.Join('/', reference.Split('/').Select(part => part is "." or ".." ? part : Uri.EscapeDataString(part)));
                resolved = PackageLogicalPath.ResolveReference(oldPaths[document.PlacementId], address);
            }
            catch (ArgumentException)
            { issues.Add(new("INVALID_REFERENCE", document.PlacementId, link.ObjectPath, "A relative link cannot be resolved inside the application.")); continue; }
            if (resolved.ExternalUri is not null) continue;
            if (resolved.LogicalPath is null || !oldGroups.TryGetValue(resolved.LogicalPath, out var targets) || targets.Length != 1)
            { issues.Add(new("TARGET_NOT_UNIQUE", document.PlacementId, link.ObjectPath, "The reference requires exactly one captured delivery target.")); continue; }
            var targetId = targets[0];
            var targetDocument = byId[targetId];
            if (isUri && resolved.Fragment is { } fragment) target = Fragment(fragment, target);
            ValidateDestination(target, targetDocument, document.PlacementId, link.ObjectPath, issues);
            var oldRelative = RelativePath(oldPaths[document.PlacementId], oldPaths[targetId]);
            var newRelative = RelativePath(finalPaths[document.PlacementId], finalPaths[targetId]);
            if (oldRelative == newRelative) continue;
            var newReference = newRelative;
            if (isUri)
            {
                newReference = string.Join('/', newRelative.Split('/').Select(part => part == ".." ? part : Uri.EscapeDataString(part)));
                var suffix = reference.IndexOfAny(['?', '#']);
                if (suffix >= 0) newReference += reference[suffix..];
            }
            required.Add(document.PlacementId);
            if (link.SourcePageNumber is null || !link.ObjectPath.StartsWith("page[", StringComparison.Ordinal))
                issues.Add(new("LINK_NOT_EDITABLE", document.PlacementId, link.ObjectPath, "This reference is not a supported page annotation; adjust the rename scope."));
            impacts.Add(new(document.PlacementId, targetId, link.ObjectPath, link.SourcePageNumber, reference,
                newReference, isUri, target.PageNumber, target.NamedDestination));
        }
        foreach (var id in required.Order())
        {
            var source = byId[id];
            if (!source.Editable || source.SequenceNumber != sequence)
                issues.Add(new("SOURCE_NOT_EDITABLE", id, null, "Affected sources must be editable documents in this draft; historical content cannot be rewritten."));
            if (!confirmed.Contains(id))
                issues.Add(new("SOURCE_NOT_CONFIRMED", id, null, "The affected source must be included in the user's confirmed execution scope."));
        }
        foreach (var id in confirmed.Where(id => !byId.ContainsKey(id)))
            issues.Add(new("CONFIRMATION_NOT_FOUND", id, null, "A confirmed source is absent from the captured inventory."));
        var input = JsonSerializer.Serialize(new
        {
            Schema = "pdf-rename-plan-v1", ApplicationId = applicationId, Sequence = sequence, Revision = revision,
            Inventory = inventory.Select(item => new { item.PlacementId, item.DocumentId, item.VersionId, item.SequenceNumber,
                item.Href, Sha256 = item.Sha256.ToLowerInvariant(), item.Editable,
                item.Facts.Basics, item.Facts.NavigationComplete, item.Facts.DestinationsComplete,
                Links = item.Facts.Links.OrderBy(link => link.ObjectPath, StringComparer.Ordinal),
                Destinations = item.Facts.Destinations.OrderBy(destination => destination.Name, StringComparer.Ordinal) }),
            Changes = requested, ConfirmedSources = confirmed
        });
        return new(applicationId, sequence, revision, Array.AsReadOnly(requested), Array.AsReadOnly(confirmed),
            Array.AsReadOnly(required.Order().ToArray()), impacts.AsReadOnly(), issues.AsReadOnly(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant());
    }

    private static void ValidateDestination(PdfDestinationTarget target, PdfRenameDocument destination, Guid source,
        string objectPath, List<PdfRenameIssue> issues)
    {
        if (target.Invalid || target.UnsupportedReason is not null ||
            (target.PageNumber is { } page && (page < 1 || destination.Facts.Basics.PageCount is null || page > destination.Facts.Basics.PageCount)) ||
            (target.NamedDestination is { } named && !destination.Facts.Destinations.Any(item => item.Name == named && !item.Invalid &&
                item.PageNumber is > 0 && item.PageNumber <= destination.Facts.Basics.PageCount)))
            issues.Add(new("INVALID_DESTINATION", source, objectPath, "The link destination is invalid or cannot be verified in its captured target."));
    }

    private static PdfDestinationTarget Fragment(string fragment, PdfDestinationTarget target)
    {
        if (fragment.Length == 0) return target with { Invalid = true };
        if (!fragment.Contains('=')) return target with { NamedDestination = fragment };
        var destinations = 0;
        foreach (var parameter in fragment.Split('&'))
        {
            var pair = parameter.Split('=', 2);
            if (pair.Length != 2) return target with { Invalid = true };
            switch (pair[0])
            {
                case "page":
                    destinations++;
                    if (!int.TryParse(pair[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page))
                        return target with { Invalid = true };
                    target = target with { PageNumber = page }; break;
                case "nameddest": destinations++; target = target with { NamedDestination = pair[1], Invalid = pair[1].Length == 0 }; break;
                default: return target with { UnsupportedReason = "The PDF open parameter is not covered." };
            }
        }
        return destinations == 1 ? target : target with { Invalid = true };
    }

    private static string RelativePath(string source, string target)
    {
        var directory = source.Split('/')[..^1];
        var destination = target.Split('/');
        var common = 0;
        while (common < directory.Length && common < destination.Length && directory[common] == destination[common]) common++;
        return string.Join('/', Enumerable.Repeat("..", directory.Length - common).Concat(destination.Skip(common)));
    }
}
