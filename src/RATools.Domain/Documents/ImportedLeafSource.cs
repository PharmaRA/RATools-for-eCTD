namespace RATools.Domain.Documents;

// Original XML spelling is provenance, not permission to write an arbitrary output path.
public sealed record ImportedLeafSource(string BackboneRelativePath, string? Href, string? ModifiedFile);
