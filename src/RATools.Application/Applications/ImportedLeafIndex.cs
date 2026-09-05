using RATools.Application.Workspaces;
using RATools.Domain.Documents;

namespace RATools.Application.Applications;

internal sealed class ImportedLeafIndex(string applicationRoot)
{
    private readonly Dictionary<string, List<ImportedLeaf>> references = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ImportedLeaf>> legacyHrefs = new(StringComparer.Ordinal);

    public void Add(string sourcePath, string? href, DocumentPlacement placement, SubmissionDocument document)
    {
        var entry = new ImportedLeaf(placement, document);
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

    public ImportedLeaf? Resolve(string sourcePath, string reference, string sequenceNumber, string section)
    {
        var normalized = NormalizeHref(reference);
        if (normalized != normalized.Trim()
            || !Uri.TryCreate(FileUri(sourcePath), normalized, out var uri)
            || !uri.IsFile || uri.Query.Length > 0
            || !WorkspacePathGuard.IsInsideScope(uri.LocalPath, applicationRoot))
        {
            return null;
        }

        if (references.TryGetValue(ReferenceKey(uri), out var matches))
        {
            return UniqueHistoricalMatch(matches, sequenceNumber, section);
        }

        // Earlier workspaces stored bare document hrefs. Preserve exact, unique
        // matches without letting repeated names overwrite historical identity.
        return uri.Fragment.Length == 0 && legacyHrefs.TryGetValue(normalized, out var legacyMatches)
            ? UniqueHistoricalMatch(legacyMatches, sequenceNumber, section)
            : null;
    }

    private static ImportedLeaf? UniqueHistoricalMatch(IEnumerable<ImportedLeaf> entries, string sequenceNumber, string section)
    {
        var matches = entries.Where(entry => entry.Placement.Operation != DocumentPlacementOperation.Delete
                && string.CompareOrdinal(entry.Placement.SequenceNumber, sequenceNumber) < 0
                && string.Equals(entry.Placement.CtdSection, section, StringComparison.OrdinalIgnoreCase))
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

internal sealed record ImportedLeaf(DocumentPlacement Placement, SubmissionDocument Document);
