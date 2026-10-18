namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class PackageValidationInputRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string? RootRelativePath { get; set; }
    public string SourceKind { get; set; } = "";
    public string InputDigest { get; set; } = "";
    public string ManifestJson { get; set; } = "";
    public string LimitsJson { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public bool IsOwnedUpload { get; set; }
}

public sealed class PackageValidationReportRecord
{
    public Guid RunId { get; set; }
    public string Json { get; set; } = "";
}

public sealed class PackageValidationFindingRecord
{
    public Guid RunId { get; set; }
    public int Ordinal { get; set; }
    public string RuleId { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Status { get; set; } = "";
    public string? LogicalPath { get; set; }
    public string? NodePath { get; set; }
    public string Json { get; set; } = "";
}

// The cumulative view is archived beside the report so later reads cannot be
// re-derived from a changed workspace. It is evidence, not a mutable current state.
public sealed class PackageValidationProjectionRecord
{
    public Guid RunId { get; set; }
    public string Json { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class PackageValidationExternalComparisonRecord
{
    public Guid RunId { get; set; }
    public Guid Id { get; set; }
    public string ToolName { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public string PackageDigest { get; set; } = "";
    public string ReportSha256 { get; set; } = "";
    public string Digest { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
    public string DifferencesJson { get; set; } = "";
    public string ReportText { get; set; } = "";
}
