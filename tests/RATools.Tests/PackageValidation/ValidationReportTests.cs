using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class ValidationReportTests
{
    private static readonly string Digest = new('a', 64);
    private static readonly string OtherDigest = new('b', 64);
    private const string Profile = "test-profile";

    private static ValidationRuleDescriptor Rule(ImplementationStatus implementation = ImplementationStatus.Implemented,
        bool manual = false, bool required = true, ValidationSeverity severity = ValidationSeverity.Error,
        RuleApplicability applicability = RuleApplicability.Always, string id = "RULE") => new(id, null, "source",
        "Test contract", "Test rule", "Test", "1", [Profile], "Explicit test scope", applicability, severity,
        required, manual, implementation, "Test evidence", ["positive"], ["negative"], [], []);

    private static ValidationRuleCatalog Catalog(ValidationRuleDescriptor? rule = null, bool qualified = true) => new("test-v1", Digest,
        [new("source", RuleSourceKind.ProjectPolicy, "1", "https://example.invalid/test-policy", Digest, "Verified")],
        [new(Profile, "test", "Scoped test profile", qualified, qualified ? "Test qualification" : "Qualification pending")], [rule ?? Rule()]);

    private static ValidationBinding Binding(string? input = null, string? history = null, string? profile = null,
        string? rules = null, string engine = "test-engine-v1", PackageValidationMode mode = PackageValidationMode.Formal,
        string? limits = null) => new(input ?? Digest, history ?? Digest, profile ?? Profile, rules ?? Digest, engine, mode, limits ?? Digest);

    private static ManualCheckEvidence Evidence(ValidationBinding? binding = null, CheckStatus outcome = CheckStatus.Pass) => new(
        "evidence-1", binding ?? Binding(), outcome, "Reviewed exact input", "reviewer-1", new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));

    private static ValidationReport Report(ValidationRuleDescriptor rule, RuleCheckResult? result = null, RuleCheckResult? other = null) =>
        ValidationReport.Create(Catalog(rule), Binding(), ValidationRunStatus.Completed, new[] { result, other }.OfType<RuleCheckResult>());

    [Fact]
    public void MissingNecessaryWarningBlocksEvenAfterSuccessfulExecution()
    {
        var report = Report(Rule(severity: ValidationSeverity.Warning));
        Assert.True(report.ExecutionCompleted);
        Assert.False(report.IsReadyForFinalization);
        Assert.Equal(1, report.Summary.NotEvaluated);
        Assert.True(Assert.Single(report.Coverage).BlocksReadiness);
    }

    [Theory]
    [InlineData(ImplementationStatus.Partial)]
    [InlineData(ImplementationStatus.NotImplemented)]
    [InlineData(ImplementationStatus.Manual)]
    [InlineData(ImplementationStatus.NotApplicable)]
    public void PartialOrAbsentImplementationCannotBecomeCompleteFromPassingObservations(ImplementationStatus implementation)
    {
        var finding = new ValidationFinding("RULE", CheckStatus.Pass, ValidationSeverity.Info, "COMPONENT_OK", "Component passed", new());
        var report = Report(Rule(implementation, manual: implementation == ImplementationStatus.Manual),
            new("RULE", CheckStatus.Pass, "Observation only", [finding]));
        Assert.False(report.IsReadyForFinalization);
        var coverage = Assert.Single(report.Coverage);
        Assert.Equal(CheckStatus.NotEvaluated, coverage.CheckStatus);
        Assert.Same(finding, Assert.Single(coverage.Findings));
        Assert.Equal(0, report.Summary.Passed);
    }

    [Fact]
    public void CompletedRunWithRuleFailuresIsAValidReportButNotReady()
    {
        var report = Report(Rule(), new("RULE", CheckStatus.Fail, "Actual checksum mismatch"));
        Assert.True(report.ExecutionCompleted);
        Assert.False(report.IsReadyForFinalization);
        Assert.Equal(1, report.Summary.Failed);
        Assert.Equal(1, report.Summary.BlockingRules);
    }

    [Fact]
    public void FullyImplementedScopedFormalReportCanBeReady()
    {
        var report = Report(Rule(), new("RULE", CheckStatus.Pass, "Exact file checked"));
        Assert.True(report.IsReadyForFinalization);
        Assert.Empty(report.BlockingReasons);
        Assert.Equal(1, report.Summary.Implemented);
    }

    [Theory]
    [InlineData(ValidationRunStatus.Pending)]
    [InlineData(ValidationRunStatus.Running)]
    [InlineData(ValidationRunStatus.Failed)]
    [InlineData(ValidationRunStatus.Cancelled)]
    [InlineData(ValidationRunStatus.InputChanged)]
    public void UnfinishedOrInvalidatedExecutionNeverAuthorizesFinalization(ValidationRunStatus status)
    {
        var report = ValidationReport.Create(Catalog(), Binding(), status, [new("RULE", CheckStatus.Pass, "Passed before termination")]);
        Assert.False(report.ExecutionCompleted);
        Assert.False(report.IsReadyForFinalization);
        Assert.Contains(report.BlockingReasons, reason => reason.StartsWith("RunNotCompleted:", StringComparison.Ordinal));
    }

    [Fact]
    public void DevelopmentModeAndUnqualifiedProfileBlockOtherwisePassingChecks()
    {
        var report = ValidationReport.Create(Catalog(qualified: false), Binding(mode: PackageValidationMode.Development),
            ValidationRunStatus.Completed, [new("RULE", CheckStatus.Pass, "All observations passed")]);
        Assert.Equal(0, report.Summary.BlockingRules);
        Assert.Equal(2, report.BlockingReasons.Count);
        Assert.False(report.IsReadyForFinalization);
    }

    [Fact]
    public void NotApplicableRequiresReasonAndAConditionalRule()
    {
        Assert.Throws<ArgumentException>(() => new RuleCheckResult("RULE", CheckStatus.NotApplicable, " "));
        Assert.Throws<ArgumentException>(() => Report(Rule(), new("RULE", CheckStatus.NotApplicable, "Bypass")));
        var report = Report(Rule(ImplementationStatus.NotImplemented, applicability: RuleApplicability.PdfPresent),
            new("RULE", CheckStatus.NotApplicable, "The captured input contains no PDF files."));
        Assert.True(report.IsReadyForFinalization);
        Assert.Equal(1, report.Summary.NotApplicable);
    }

    [Theory]
    [InlineData(CheckStatus.Pass, CheckStatus.Fail)]
    [InlineData(CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData(CheckStatus.NotEvaluated, CheckStatus.Fail)]
    [InlineData(CheckStatus.NotApplicable, CheckStatus.Pass)]
    public void SummaryCannotConcealFailedOrIncompleteObservations(CheckStatus summary, CheckStatus observation)
    {
        Assert.Throws<ArgumentException>(() => new RuleCheckResult("RULE", summary, "Invalid summary",
            [new("RULE", observation, ValidationSeverity.Error, "OBSERVATION", "Observation", new())]));
    }

    [Fact]
    public void FindingSeverityCanEscalateANonRequiredWarningToABlockingError()
    {
        var report = Report(Rule(required: false, severity: ValidationSeverity.Warning),
            new("RULE", CheckStatus.Fail, "Failed", [new("RULE", CheckStatus.Fail, ValidationSeverity.Error,
                "READ_ERROR", "Cannot read", new("0001", "0001/index.xml", "index.xml", "leaf-1", Line: 12, Column: 3))]));
        Assert.False(report.IsReadyForFinalization);
        Assert.Equal(ValidationSeverity.Error, Assert.Single(report.Coverage).Severity);
        var warning = Report(Rule(required: false, severity: ValidationSeverity.Warning), new("RULE", CheckStatus.Fail, "Optional advisory"));
        Assert.True(warning.IsReadyForFinalization);
    }

    [Fact]
    public void ManualEvidenceRequiresExplicitRulePermission()
    {
        Assert.Throws<ArgumentException>(() => Report(Rule(ImplementationStatus.Partial),
            new("RULE", CheckStatus.Pass, "Manual", manualEvidence: Evidence())));
        var report = Report(Rule(ImplementationStatus.Manual, manual: true),
            new("RULE", CheckStatus.Pass, "Manual", manualEvidence: Evidence()));
        Assert.True(report.IsReadyForFinalization);
        Assert.Equal(Evidence(), Assert.Single(report.Coverage).ManualEvidence);
        Assert.Equal(1, report.Summary.Manual);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("history")]
    [InlineData("profile")]
    [InlineData("rules")]
    [InlineData("engine")]
    [InlineData("mode")]
    [InlineData("limits")]
    public void ManualEvidenceBindsEveryReuseDimension(string dimension)
    {
        var changed = dimension switch
        {
            "input" => Binding(input: OtherDigest),
            "history" => Binding(history: OtherDigest),
            "profile" => Binding(profile: "other-profile"),
            "rules" => Binding(rules: OtherDigest),
            "engine" => Binding(engine: "other-engine"),
            "mode" => Binding(mode: PackageValidationMode.Development),
            "limits" => Binding(limits: OtherDigest),
            _ => throw new ArgumentException("Unknown dimension")
        };
        Assert.Throws<ArgumentException>(() => Report(Rule(ImplementationStatus.Manual, manual: true),
            new("RULE", CheckStatus.Pass, "Wrong binding", manualEvidence: Evidence(changed))));
    }

    [Fact]
    public void ManualEvidenceRequiresAccountabilityAndMatchingOutcome()
    {
        ManualCheckEvidence[] invalid = [Evidence() with { EvidenceId = "" }, Evidence() with { Reviewer = "" },
            Evidence() with { Reason = "" }, Evidence() with { RecordedUtc = default },
            Evidence() with { RecordedUtc = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(8)) }, Evidence(outcome: CheckStatus.Fail)];
        foreach (var evidence in invalid)
            Assert.Throws<ArgumentException>(() => Report(Rule(ImplementationStatus.Manual, manual: true),
                new("RULE", CheckStatus.Pass, "Manual", manualEvidence: evidence)));
        var failed = Report(Rule(ImplementationStatus.Manual, manual: true),
            new("RULE", CheckStatus.Fail, "Manual review failed", manualEvidence: Evidence(outcome: CheckStatus.Fail)));
        Assert.False(failed.IsReadyForFinalization);
    }

    [Fact]
    public void UnknownDuplicateOrOutOfScopeChecksAndWrongCatalogDigestAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Report(Rule(), new("UNKNOWN", CheckStatus.Pass, "Unknown")));
        Assert.Throws<ArgumentException>(() => Report(Rule(), new("RULE", CheckStatus.Pass, "One"), new("RULE", CheckStatus.Fail, "Two")));
        Assert.Throws<ArgumentException>(() => ValidationReport.Create(Catalog(), Binding(rules: OtherDigest), ValidationRunStatus.Completed, []));
        var catalog = PackageValidationCatalog.Current;
        Assert.Throws<ArgumentException>(() => ValidationReport.Create(catalog,
            Binding(profile: PackageValidationCatalog.IchProfile, rules: catalog.Digest), ValidationRunStatus.Completed,
            [new("US-REGIONAL-DTD", CheckStatus.Pass, "Out of scope")]));
    }

    [Fact]
    public void SerializedReportPreservesWireEnumsCoverageLocationAndSevenPartBinding()
    {
        var report = Report(Rule(), new("RULE", CheckStatus.Fail, "Mismatch", [new("RULE", CheckStatus.Fail,
            ValidationSeverity.Error, "HASH_MISMATCH", "Digest differs", new("0001", "0001/index.xml", "index.xml", "leaf-1", Line: 8, Column: 2),
            "expected-hash", "actual-hash", "Restore bytes")]));
        using var json = JsonDocument.Parse(PackageValidationCatalog.SerializeReport(report));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Completed", json.RootElement.GetProperty("runStatus").GetString());
        Assert.False(json.RootElement.GetProperty("isReadyForFinalization").GetBoolean());
        Assert.Equal(7, json.RootElement.GetProperty("binding").EnumerateObject().Count());
        var coverage = json.RootElement.GetProperty("coverage")[0];
        Assert.Equal("Fail", coverage.GetProperty("checkStatus").GetString());
        Assert.Equal("Implemented", coverage.GetProperty("implementationStatus").GetString());
        var finding = coverage.GetProperty("findings")[0];
        Assert.Equal(8, finding.GetProperty("location").GetProperty("line").GetInt32());
        Assert.Equal("leaf-1", finding.GetProperty("location").GetProperty("leafId").GetString());
        Assert.Equal("Restore bytes", finding.GetProperty("recommendedAction").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha256:a")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void NonCanonicalDigestsAreRejected(string digest) => Assert.Throws<ArgumentException>(() => Binding(input: digest));
}
