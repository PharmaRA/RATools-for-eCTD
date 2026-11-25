using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed record PackagePdfDocument(string LogicalPath, PackagePdfFacts? Facts, string? CapturedSha256 = null);
public sealed record PackagePdfLinkResolution(string SourceLogicalPath, PdfNavigationLink Link,
    string? TargetLogicalPath, CheckStatus Status);

public sealed class PackagePdfInspection
{
    internal PackagePdfInspection(PackageFileInspection files, string inspectorVersion, IEnumerable<PackagePdfDocument> documents,
        IEnumerable<PackagePdfLinkResolution> links, IEnumerable<RuleCheckResult> checks,
        IEnumerable<PackageFormTemplateMatch>? formTemplates = null)
    {
        InputDigest = files.InputDigest;
        HistoryManifestDigest = files.HistoryManifestDigest;
        ProfileSnapshotId = files.ProfileSnapshotId;
        LimitsDigest = files.LimitsDigest;
        EngineVersion = "package-pdf-validator-v26;" + inspectorVersion;
        Documents = Array.AsReadOnly(documents.ToArray());
        Links = Array.AsReadOnly(links.ToArray());
        Checks = Array.AsReadOnly(checks.ToArray());
        FormTemplates = Array.AsReadOnly((formTemplates ?? []).ToArray());
        RulesDigest = files.RulesDigest;
    }
    public int SchemaVersion { get; } = 1;
    public string EngineVersion { get; }
    public string RulesDigest { get; }
    public string InputDigest { get; }
    public string HistoryManifestDigest { get; }
    public string ProfileSnapshotId { get; }
    public string LimitsDigest { get; }
    public IReadOnlyList<PackagePdfDocument> Documents { get; }
    public IReadOnlyList<PackagePdfLinkResolution> Links { get; }
    public IReadOnlyList<RuleCheckResult> Checks { get; }
    public IReadOnlyList<PackageFormTemplateMatch> FormTemplates { get; }
}
