using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace RATools.Domain.PackageValidation;

public enum CheckStatus { Pass, Fail, NotEvaluated, NotApplicable }
public enum ImplementationStatus { Implemented, Partial, Manual, NotImplemented, NotApplicable }
public enum ValidationSeverity { Info, Warning, Error }
public enum ValidationRunStatus { Pending, Running, Completed, Failed, Cancelled, InputChanged }
public enum PackageValidationMode { Development, Formal }
public enum RuleApplicability { Always, ZipPresent, PdfPresent, LifecycleOperationsPresent, RegionalBackbonePresent }
public enum RuleSourceKind { RegulatorySpecification, ProjectPolicy }

public sealed record ValidationRuleSource(string Id, RuleSourceKind Kind, string Version, string Url,
    string? Sha256, string VerificationStatus);

public sealed record ValidationProfileScope(string ProfileSnapshotId, string TemplateKey, string Description,
    bool QualifiedForFinalization, string QualificationReason);

public sealed class ValidationRuleDescriptor
{
    public ValidationRuleDescriptor(string internalRuleId, string? authorityRuleId, string sourceId, string sourceSection,
        string title, string category, string ruleVersion, IEnumerable<string> profileSnapshotIds, string applicableScope,
        RuleApplicability applicability, ValidationSeverity severity, bool requiredForReadiness, bool allowsManualClosure,
        ImplementationStatus implementationStatus, string implementationNote, IEnumerable<string> positiveFixtures,
        IEnumerable<string> negativeFixtures, IEnumerable<string> componentEvidence, IEnumerable<string> externalComparisonEvidence)
    {
        foreach (var value in new[] { internalRuleId, sourceId, sourceSection, title, category, ruleVersion, applicableScope, implementationNote })
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Enum.IsDefined(applicability) || !Enum.IsDefined(severity) || !Enum.IsDefined(implementationStatus))
            throw new ArgumentException("Rule enum values must belong to the versioned contract.");
        InternalRuleId = internalRuleId;
        if (authorityRuleId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(authorityRuleId);
        AuthorityRuleId = authorityRuleId;
        SourceId = sourceId;
        SourceSection = sourceSection;
        Title = title;
        Category = category;
        RuleVersion = ruleVersion;
        ProfileSnapshotIds = Freeze(profileSnapshotIds);
        if (ProfileSnapshotIds.Count == 0) throw new ArgumentException("A rule requires an explicit profile scope.", nameof(profileSnapshotIds));
        ApplicableScope = applicableScope;
        Applicability = applicability;
        Severity = severity;
        RequiredForReadiness = requiredForReadiness;
        AllowsManualClosure = allowsManualClosure;
        ImplementationStatus = implementationStatus;
        ImplementationNote = implementationNote;
        PositiveFixtures = Freeze(positiveFixtures);
        NegativeFixtures = Freeze(negativeFixtures);
        ComponentEvidence = Freeze(componentEvidence);
        ExternalComparisonEvidence = Freeze(externalComparisonEvidence);
        if (implementationStatus == ImplementationStatus.Implemented && (PositiveFixtures.Count == 0 || NegativeFixtures.Count == 0))
            throw new ArgumentException("Implemented rules require both positive and negative test evidence.");
        if (implementationStatus == ImplementationStatus.Manual && !allowsManualClosure)
            throw new ArgumentException("A manual rule must explicitly permit evidence-based closure.");
    }

    public string InternalRuleId { get; }
    public string? AuthorityRuleId { get; }
    public string SourceId { get; }
    public string SourceSection { get; }
    public string Title { get; }
    public string Category { get; }
    public string RuleVersion { get; }
    public IReadOnlyList<string> ProfileSnapshotIds { get; }
    public string ApplicableScope { get; }
    public RuleApplicability Applicability { get; }
    public ValidationSeverity Severity { get; }
    public bool RequiredForReadiness { get; }
    public bool AllowsManualClosure { get; }
    public ImplementationStatus ImplementationStatus { get; }
    public string ImplementationNote { get; }
    public IReadOnlyList<string> PositiveFixtures { get; }
    public IReadOnlyList<string> NegativeFixtures { get; }
    public IReadOnlyList<string> ComponentEvidence { get; }
    public IReadOnlyList<string> ExternalComparisonEvidence { get; }

    private static ReadOnlyCollection<string> Freeze(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Any(string.IsNullOrWhiteSpace) || copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Rule lists must contain distinct nonblank values.", nameof(values));
        return Array.AsReadOnly(copy);
    }
}

public sealed class ValidationRuleCatalog
{
    public ValidationRuleCatalog(string version, string digest, IEnumerable<ValidationRuleSource> sources,
        IEnumerable<ValidationProfileScope> profiles, IEnumerable<ValidationRuleDescriptor> rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ValidationBinding.RequireDigest(digest);
        Version = version;
        Digest = digest;
        Sources = sources.ToFrozenDictionary(source => source.Id, StringComparer.Ordinal);
        Profiles = profiles.ToFrozenDictionary(profile => profile.ProfileSnapshotId, StringComparer.Ordinal);
        Rules = rules.ToFrozenDictionary(rule => rule.InternalRuleId, StringComparer.Ordinal);
        if (Profiles.Count == 0 || Rules.Count == 0) throw new ArgumentException("A rule catalog cannot be empty.");
        foreach (var source in Sources.Values)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.Version) ||
                !Enum.IsDefined(source.Kind) || !Uri.TryCreate(source.Url, UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps || source.VerificationStatus is not ("Verified" or "Unverified"))
                throw new ArgumentException("Sources require explicit versioned HTTPS provenance and verification status.");
            if (source.Sha256 is not null) ValidationBinding.RequireDigest(source.Sha256);
            if (source.VerificationStatus == "Verified" && source.Sha256 is null)
                throw new ArgumentException("Verified sources require a pinned content digest.");
        }
        foreach (var profile in Profiles.Values)
        {
            if (string.IsNullOrWhiteSpace(profile.ProfileSnapshotId) || string.IsNullOrWhiteSpace(profile.TemplateKey) ||
                string.IsNullOrWhiteSpace(profile.Description) || string.IsNullOrWhiteSpace(profile.QualificationReason))
                throw new ArgumentException("Profile scope and qualification rationale must be explicit.");
            if (!Rules.Values.Any(rule => rule.ProfileSnapshotIds.Contains(profile.ProfileSnapshotId, StringComparer.Ordinal)))
                throw new ArgumentException("Each declared profile needs a nonempty rule inventory.");
        }
        foreach (var rule in Rules.Values)
            if (!Sources.ContainsKey(rule.SourceId) || rule.ProfileSnapshotIds.Any(profile => !Profiles.ContainsKey(profile)))
                throw new ArgumentException("Every rule source and profile must resolve within this catalog.");
        foreach (var profile in Profiles.Values.Where(profile => profile.QualifiedForFinalization))
            if (ForProfile(profile.ProfileSnapshotId).Any(rule => rule.RequiredForReadiness &&
                Sources[rule.SourceId].VerificationStatus != "Verified"))
                throw new ArgumentException("A qualified profile cannot depend on unverified mandatory rule sources.");
    }

    public string Version { get; }
    public string Digest { get; }
    public IReadOnlyDictionary<string, ValidationRuleSource> Sources { get; }
    public IReadOnlyDictionary<string, ValidationProfileScope> Profiles { get; }
    public IReadOnlyDictionary<string, ValidationRuleDescriptor> Rules { get; }

    public IReadOnlyList<ValidationRuleDescriptor> ForProfile(string profileSnapshotId)
    {
        if (!Profiles.ContainsKey(profileSnapshotId)) throw new ArgumentException("Select an explicit catalog profile snapshot.", nameof(profileSnapshotId));
        return Rules.Values.Where(rule => rule.ProfileSnapshotIds.Contains(profileSnapshotId, StringComparer.Ordinal))
            .OrderBy(rule => rule.InternalRuleId, StringComparer.Ordinal).ToArray();
    }
}
