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
