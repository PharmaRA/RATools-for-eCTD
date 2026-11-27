namespace RATools.Application.PackageValidation;

internal enum DefineXmlReadStatus { Read, Invalid, Unsupported, InputMismatch, LimitExceeded }
internal enum DefineReferenceStatus { Resolved, Unspecified, Missing, Ambiguous }
internal sealed record DefineXmlReadLimits(int MaxBytes = 8 * 1024 * 1024, int MaxNodes = 100000,
    int MaxDepth = 128, long MaxCharacters = 8 * 1024 * 1024);
internal sealed record DefineStandardDeclaration(string? Oid, string? Name, string? Type, string? Version, string? Status);
internal sealed record DefineLeafDeclaration(string? Id, string? Href);
internal sealed record DefineDatasetDeclaration(string? Oid, string? Name, string? SasDatasetName, string? Domain,
    string? StandardOid, string? ArchiveLocationId, string? IsNonStandard, string? HasNoData,
    DefineReferenceStatus StandardReference, DefineStandardDeclaration? Standard,
    DefineReferenceStatus FileReference, DefineLeafDeclaration? File);

// Decoded declarations only. ReadComplete is not XSD validation, CDISC conformance or FDA applicability.
internal sealed class PackageDefineXmlFacts(DefineXmlReadStatus status, string? sha256, bool readComplete = false,
    string? studyOid = null, string? metadataOid = null, string? defineVersion = null,
    IEnumerable<DefineStandardDeclaration>? standards = null, IEnumerable<DefineLeafDeclaration>? leaves = null,
    IEnumerable<DefineDatasetDeclaration>? datasets = null)
{
    internal DefineXmlReadStatus Status { get; } = status;
    internal string? Sha256 { get; } = sha256;
    internal bool ReadComplete { get; } = readComplete;
    internal string? StudyOid { get; } = studyOid;
    internal string? MetadataOid { get; } = metadataOid;
    internal string? DefineVersion { get; } = defineVersion;
    internal IReadOnlyList<DefineStandardDeclaration> Standards { get; } = Array.AsReadOnly((standards ?? []).ToArray());
    internal IReadOnlyList<DefineLeafDeclaration> Leaves { get; } = Array.AsReadOnly((leaves ?? []).ToArray());
    internal IReadOnlyList<DefineDatasetDeclaration> Datasets { get; } = Array.AsReadOnly((datasets ?? []).ToArray());
}
