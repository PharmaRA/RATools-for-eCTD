namespace RATools.Domain.PackageValidation;

public sealed record ValidationLocation(string? SequenceNumber = null, string? LogicalPath = null,
    string? BackboneRelativePath = null, string? LeafId = null, string? NodePath = null, string? FieldPath = null,
    int? Line = null, int? Column = null);

public sealed record ValidationFinding(string RuleId, CheckStatus CheckStatus, ValidationSeverity Severity,
    string Code, string Message, ValidationLocation Location, string? Expected = null, string? Actual = null,
    string? RecommendedAction = null);

public sealed record ManualCheckEvidence(string EvidenceId, ValidationBinding Binding, CheckStatus Outcome,
    string Reason, string Reviewer, DateTimeOffset RecordedUtc);

public sealed class RuleCheckResult
{
    public RuleCheckResult(string ruleId, CheckStatus status, string reason,
        IEnumerable<ValidationFinding>? findings = null, ManualCheckEvidence? manualEvidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        RuleId = ruleId;
        Status = status;
        Reason = reason;
        var copy = (findings ?? []).ToArray();
        if (copy.Any(finding => finding.RuleId != ruleId || !Enum.IsDefined(finding.CheckStatus) || !Enum.IsDefined(finding.Severity) ||
            string.IsNullOrWhiteSpace(finding.Code) || string.IsNullOrWhiteSpace(finding.Message) || finding.Location is null ||
            finding.Location.Line is < 1 || finding.Location.Column is < 1))
            throw new ArgumentException("Findings require the matching rule and valid status, severity and location.", nameof(findings));
        if (copy.Any(finding => finding.CheckStatus == CheckStatus.Fail) && status != CheckStatus.Fail ||
            status == CheckStatus.Pass && copy.Any(finding => finding.CheckStatus == CheckStatus.NotEvaluated) ||
            status == CheckStatus.NotApplicable && copy.Any(finding => finding.CheckStatus != CheckStatus.NotApplicable))
            throw new ArgumentException("A rule summary must preserve failures and incomplete observations.", nameof(status));
        Findings = Array.AsReadOnly(copy);
        ManualEvidence = manualEvidence;
    }

    public string RuleId { get; }
    public CheckStatus Status { get; }
    public string Reason { get; }
    public IReadOnlyList<ValidationFinding> Findings { get; }
    public ManualCheckEvidence? ManualEvidence { get; }
}

public sealed record RuleCoverage(string RuleId, ImplementationStatus ImplementationStatus, CheckStatus CheckStatus,
    ValidationSeverity Severity, bool RequiredForReadiness, bool BlocksReadiness, string Reason,
    IReadOnlyList<ValidationFinding> Findings, ManualCheckEvidence? ManualEvidence);

public sealed record ValidationCoverageSummary(int TotalRules, int Passed, int Failed, int NotEvaluated,
    int NotApplicable, int BlockingRules, int Implemented, int Partial, int Manual, int NotImplemented, int NotApplicableImplementation);

public sealed class ValidationReport
{
    private ValidationReport(ValidationBinding binding, ValidationRunStatus status, IReadOnlyList<RuleCoverage> coverage,
        ValidationCoverageSummary summary, IReadOnlyList<string> blockingReasons)
    {
        Binding = binding;
        RunStatus = status;
        Coverage = coverage;
        Summary = summary;
        BlockingReasons = blockingReasons;
    }

    public int SchemaVersion { get; } = 1;
    public ValidationBinding Binding { get; }
    public ValidationRunStatus RunStatus { get; }
    public IReadOnlyList<RuleCoverage> Coverage { get; }
    public ValidationCoverageSummary Summary { get; }
    public IReadOnlyList<string> BlockingReasons { get; }
    public bool ExecutionCompleted => RunStatus == ValidationRunStatus.Completed;
    public bool IsReadyForFinalization => BlockingReasons.Count == 0;

    public static ValidationReport Create(ValidationRuleCatalog catalog, ValidationBinding binding, ValidationRunStatus status,
        IEnumerable<RuleCheckResult> results)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(binding);
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (binding.RulesDigest != catalog.Digest) throw new ArgumentException("The report must bind this exact rule catalog.", nameof(binding));
        var rules = catalog.ForProfile(binding.ProfileSnapshotId);
        var supplied = results.ToDictionary(result => result.RuleId, StringComparer.Ordinal);
        var selectedIds = rules.Select(rule => rule.InternalRuleId).ToHashSet(StringComparer.Ordinal);
        if (supplied.Keys.Any(id => !selectedIds.Contains(id))) throw new ArgumentException("The report contains an unknown or out-of-scope rule.", nameof(results));
        var coverage = new List<RuleCoverage>();
        foreach (var rule in rules)
        {
            supplied.TryGetValue(rule.InternalRuleId, out var check);
            var checkStatus = check?.Status ?? CheckStatus.NotEvaluated;
            var reason = check?.Reason ?? "The selected rule was not executed.";
            if (checkStatus == CheckStatus.NotApplicable && rule.Applicability == RuleApplicability.Always)
                throw new ArgumentException($"Rule '{rule.InternalRuleId}' is always applicable in its selected profile.", nameof(results));
            if (check?.ManualEvidence is { } manual)
            {
                if (!rule.AllowsManualClosure || manual.Binding != binding || manual.Outcome != checkStatus ||
                    manual.Outcome is not (CheckStatus.Pass or CheckStatus.Fail) || string.IsNullOrWhiteSpace(manual.EvidenceId) ||
                    string.IsNullOrWhiteSpace(manual.Reason) || string.IsNullOrWhiteSpace(manual.Reviewer) ||
                    manual.RecordedUtc.Offset != TimeSpan.Zero || manual.RecordedUtc == default)
                    throw new ArgumentException($"Manual evidence for '{rule.InternalRuleId}' is not authorized for this rule and exact input binding.", nameof(results));
            }
            else if (checkStatus == CheckStatus.Pass && rule.ImplementationStatus != ImplementationStatus.Implemented)
            {
                // Keep component observations, but never turn partial or absent
                // implementation into full-rule coverage just because nothing failed.
                checkStatus = CheckStatus.NotEvaluated;
                reason = $"{rule.ImplementationStatus}: {rule.ImplementationNote} Observed result: {reason}";
            }
            var severity = check?.Findings.Aggregate(rule.Severity, (current, finding) => finding.Severity > current ? finding.Severity : current) ?? rule.Severity;
            var blocks = checkStatus == CheckStatus.Fail && (rule.RequiredForReadiness || severity == ValidationSeverity.Error) ||
                checkStatus == CheckStatus.NotEvaluated && rule.RequiredForReadiness;
            coverage.Add(new(rule.InternalRuleId, rule.ImplementationStatus, checkStatus, severity,
                rule.RequiredForReadiness, blocks, reason, check?.Findings ?? [], check?.ManualEvidence));
        }
        var summary = new ValidationCoverageSummary(coverage.Count,
            coverage.Count(rule => rule.CheckStatus == CheckStatus.Pass), coverage.Count(rule => rule.CheckStatus == CheckStatus.Fail),
            coverage.Count(rule => rule.CheckStatus == CheckStatus.NotEvaluated), coverage.Count(rule => rule.CheckStatus == CheckStatus.NotApplicable),
            coverage.Count(rule => rule.BlocksReadiness), coverage.Count(rule => rule.ImplementationStatus == ImplementationStatus.Implemented),
            coverage.Count(rule => rule.ImplementationStatus == ImplementationStatus.Partial), coverage.Count(rule => rule.ImplementationStatus == ImplementationStatus.Manual),
            coverage.Count(rule => rule.ImplementationStatus == ImplementationStatus.NotImplemented), coverage.Count(rule => rule.ImplementationStatus == ImplementationStatus.NotApplicable));
        var reasons = coverage.Where(rule => rule.BlocksReadiness).Select(rule => $"{rule.RuleId}: {rule.Reason}").ToList();
        if (status != ValidationRunStatus.Completed) reasons.Add($"RunNotCompleted: {status}");
        if (binding.ValidationMode != PackageValidationMode.Formal) reasons.Add("DevelopmentMode: This run cannot authorize finalization.");
        var profile = catalog.Profiles[binding.ProfileSnapshotId];
        if (!profile.QualifiedForFinalization) reasons.Add($"ProfileNotQualified: {profile.QualificationReason}");
        return new ValidationReport(binding, status, coverage.AsReadOnly(), summary, reasons.AsReadOnly());
    }
}
