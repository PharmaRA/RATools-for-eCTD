using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Globalization;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public static class PackageValidationCatalog
{
    public const string Version = "package-rules-v1";
    public const string IchProfile = "ich-3.2.2-development-v1";
    public const string UsProfile = "us-fda-3.2.2-m1-3.3-development-v1";
    public const string EuProfile = "eu-3.2.2-m1-3.1.1-development-v1";
    private static readonly Lazy<ValidationRuleCatalog> Snapshot = new(LoadEmbedded);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { RequireCatalogMembers }
        }
    };
    public static ValidationRuleCatalog Current => Snapshot.Value;

    public static ValidationRuleCatalog Load(Stream stream)
    {
        using var json = JsonDocument.Parse(stream);
        // Reject duplicate properties and unsupported canonical values before
        // deserializing; the full catalog participates in report reuse identity.
        var digest = CanonicalJson.Digest(json.RootElement);
        var file = json.RootElement.Deserialize<CatalogFile>(JsonOptions)
            ?? throw new InvalidOperationException("The package validation catalog is empty.");
        if (file.SchemaVersion != 1 || file.CatalogVersion != Version)
            throw new InvalidOperationException("Unsupported package rule catalog version.");
        ArgumentException.ThrowIfNullOrWhiteSpace(file.Purpose);
        if (!DateOnly.TryParseExact(file.ReviewedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("The catalog requires an explicit review date.");
        ArgumentNullException.ThrowIfNull(file.Sources);
        ArgumentNullException.ThrowIfNull(file.Profiles);
        ArgumentNullException.ThrowIfNull(file.Rules);
        if (file.Sources.Any(source => source is null) || file.Profiles.Any(profile => profile is null) || file.Rules.Any(rule => rule is null))
            throw new ArgumentException("Catalog inventories cannot contain null entries.");
        return new ValidationRuleCatalog(file.CatalogVersion, digest, file.Sources, file.Profiles,
            file.Rules.Select(rule => new ValidationRuleDescriptor(rule.InternalRuleId, rule.AuthorityRuleId,
                rule.SourceId, rule.SourceSection, rule.Title, rule.Category, rule.RuleVersion, rule.ProfileSnapshotIds,
                rule.ApplicableScope, rule.Applicability, rule.Severity, rule.RequiredForReadiness, rule.AllowsManualClosure,
                rule.ImplementationStatus, rule.ImplementationNote, rule.PositiveFixtures, rule.NegativeFixtures,
                rule.ComponentEvidence, rule.ExternalComparisonEvidence)));
    }

    public static string SerializeReport(ValidationReport report) => JsonSerializer.Serialize(report, JsonOptions);

    private static void RequireCatalogMembers(JsonTypeInfo type)
    {
        if (type.Type == typeof(CatalogFile) || type.Type == typeof(RuleFile) ||
            type.Type == typeof(ValidationRuleSource) || type.Type == typeof(ValidationProfileScope))
            foreach (var property in type.Properties) property.IsRequired = true;
    }

    private static ValidationRuleCatalog LoadEmbedded()
    {
        using var stream = typeof(PackageValidationCatalog).Assembly.GetManifestResourceStream("RATools.PackageValidationCatalog")
            ?? throw new InvalidOperationException("The pinned package validation catalog is missing.");
        return Load(stream);
    }

    private sealed record CatalogFile(int SchemaVersion, string CatalogVersion, string Purpose, string ReviewedOn,
        ValidationRuleSource[] Sources, ValidationProfileScope[] Profiles, RuleFile[] Rules);

    private sealed record RuleFile(string InternalRuleId, string? AuthorityRuleId, string SourceId, string SourceSection,
        string Title, string Category, string RuleVersion, string[] ProfileSnapshotIds, string ApplicableScope,
        RuleApplicability Applicability, ValidationSeverity Severity, bool RequiredForReadiness, bool AllowsManualClosure,
        ImplementationStatus ImplementationStatus, string ImplementationNote, string[] PositiveFixtures,
        string[] NegativeFixtures, string[] ComponentEvidence, string[] ExternalComparisonEvidence);
}
