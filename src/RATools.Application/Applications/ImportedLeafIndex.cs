using RATools.Application.Workspaces;
using RATools.Domain.Documents;

namespace RATools.Application.Applications;

internal sealed class ImportedLeafIndex(string applicationRoot)
{
    private readonly Dictionary<string, List<ImportedLeaf>> references = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ImportedLeaf>> legacyHrefs = new(StringComparer.Ordinal);

    public void Add(string sourcePath, string? href, DocumentPlacement placement, SubmissionDocument document, string? unboundContext)
    {
        var entry = new ImportedLeaf(placement, document, unboundContext);
        Add(references, ReferenceKey(new Uri(FileUri(sourcePath), $"#{Uri.EscapeDataString(placement.LeafId)}")), entry);
        if (placement.Operation != DocumentPlacementOperation.Delete)
        {
            Add(references, ReferenceKey(FileUri(document.StoragePath)), entry);
            if (!string.IsNullOrWhiteSpace(href))
            {
                Add(legacyHrefs, NormalizeHref(href), entry);
            }
        }
    }

    public ImportedLeaf? ResolveAddress(string sourcePath, string reference, string sequenceNumber)
    {
        var uri = ResolveUri(sourcePath, reference);
        return uri is not null && references.TryGetValue(ReferenceKey(uri), out var matches)
            ? UniqueHistoricalMatch(matches, sequenceNumber) : null;
    }

    public ImportedLeaf? Resolve(string sourcePath, string reference, string sequenceNumber, string section,
        Guid? nodeInstanceId, string? unboundContext, bool unresolvedIdentity, string backboneRelativePath)
    {
        var uri = ResolveUri(sourcePath, reference);
        if (uri is null) return null;
        bool SameContext(ImportedLeaf entry) => entry.Placement.CtdSection == section &&
            entry.Placement.NodeInstanceId == nodeInstanceId &&
            (nodeInstanceId is not null || entry.UnboundContext == unboundContext);
        if (references.TryGetValue(ReferenceKey(uri), out var matches))
        {
            // Explicit addresses are resolved before context is checked. Filtering
            // first would conceal an address that is itself ambiguous.
            var target = UniqueHistoricalMatch(matches, sequenceNumber);
            return target is not null && SameContext(target) ? target : null;
        }
        var normalized = NormalizeHref(reference);
        // A failed explicit path/fragment never falls back to a same-named file.
        return !unresolvedIdentity && uri.Fragment.Length == 0 && !normalized.Split('/').Contains("..") &&
            !Uri.TryCreate(normalized, UriKind.Absolute, out _) && legacyHrefs.TryGetValue(normalized, out var legacyMatches)
            ? UniqueHistoricalMatch(legacyMatches.Where(entry => SameContext(entry) &&
                entry.Placement.ImportedSource?.BackboneRelativePath == backboneRelativePath), sequenceNumber) : null;
    }

    private Uri? ResolveUri(string sourcePath, string reference)
    {
        var normalized = NormalizeHref(reference);
        if (normalized != normalized.Trim()
            || !Uri.TryCreate(FileUri(sourcePath), normalized, out var uri)
            || !uri.IsFile || uri.Query.Length > 0
            || !WorkspacePathGuard.IsInsideScope(uri.LocalPath, applicationRoot))
        {
            return null;
        }

        return uri;
    }

    private static ImportedLeaf? UniqueHistoricalMatch(IEnumerable<ImportedLeaf> entries, string sequenceNumber)
    {
        var matches = entries.Where(entry => entry.Placement.Operation != DocumentPlacementOperation.Delete
                && string.CompareOrdinal(entry.Placement.SequenceNumber, sequenceNumber) < 0)
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static void Add(Dictionary<string, List<ImportedLeaf>> index, string key, ImportedLeaf entry)
    {
        if (!index.TryGetValue(key, out var entries))
        {
            entries = [];
            index.Add(key, entries);
        }
        entries.Add(entry);
    }

    private static Uri FileUri(string path)
        => new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = Path.GetFullPath(path) }.Uri;

    private static string ReferenceKey(Uri uri)
        => uri.GetLeftPart(UriPartial.Path) + (uri.Fragment.Length == 0
            ? string.Empty : $"#{Uri.EscapeDataString(Uri.UnescapeDataString(uri.Fragment[1..]))}");

    private static string NormalizeHref(string href)
    {
        var normalized = href.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        return normalized;
    }
}

internal sealed record ImportedLeaf(DocumentPlacement Placement, SubmissionDocument Document, string? UnboundContext);
