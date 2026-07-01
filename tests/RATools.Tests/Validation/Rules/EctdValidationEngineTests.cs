using RATools.Application.Standards;
using RATools.Application.Validation.Requests;
using RATools.Application.Validation.Rules;

namespace RATools.Tests.Validation.Rules;

public sealed class EctdValidationEngineTests
{
    [Fact]
    public void Evaluate_MapsHighAndMediumFindingsToErrorsAndLowFindingsToWarnings()
    {
        var rule = new StubRule(
            new EctdValidationFinding("HIGH-RULE", "CategoryA", EctdValidationSeverity.High, "High message", "Fix high"),
            new EctdValidationFinding("MEDIUM-RULE", "CategoryB", EctdValidationSeverity.Medium, "Medium message", "Fix medium"),
            new EctdValidationFinding("LOW-RULE", "CategoryC", EctdValidationSeverity.Low, "Low message", "Fix low"));
        var provider = new StubRuleSetProvider(rule);
        var engine = new EctdValidationEngine(provider);

        var findings = engine.Evaluate(CreateContext());

        Assert.Collection(
            findings,
            first =>
            {
                Assert.Equal("ValidationCriteria", first.Source);
                Assert.Equal("Error", first.Severity);
                Assert.Equal("HIGH-RULE", first.Code);
                Assert.Equal("CategoryA", first.Category);
                Assert.Equal("Fix high", first.RecommendedAction);
            },
            second =>
            {
                Assert.Equal("ValidationCriteria", second.Source);
                Assert.Equal("Error", second.Severity);
                Assert.Equal("MEDIUM-RULE", second.Code);
                Assert.Equal("CategoryB", second.Category);
                Assert.Equal("Fix medium", second.RecommendedAction);
            },
            third =>
            {
                Assert.Equal("ValidationCriteria", third.Source);
                Assert.Equal("Warning", third.Severity);
                Assert.Equal("LOW-RULE", third.Code);
                Assert.Equal("CategoryC", third.Category);
                Assert.Equal("Fix low", third.RecommendedAction);
            });
    }

    private static EctdValidationContext CreateContext()
    {
        var profile = new StandardsProfile(
            "us-fda-ectd-3.2.2",
            "US FDA eCTD 3.2.2",
            "FDA",
            "United States",
            "3.2.2",
            "3.3",
            "1.9",
            "4.5",
            [],
            []);
        return new EctdValidationContext(profile, new ValidateSequenceRequest(Guid.NewGuid(), "0000"), null, null);
    }

    private sealed class StubRuleSetProvider(params IEctdValidationRule[] rules) : IEctdValidationRuleSetProvider
    {
        public EctdValidationRuleSet GetRuleSet(StandardsProfile profile)
            => new(profile.TemplateKey, profile.ValidationCriteriaVersion, rules);
    }

    private sealed class StubRule(params EctdValidationFinding[] findings) : IEctdValidationRule
    {
        public string RuleId => "STUB";

        public string Category => "Stub";

        public EctdValidationSeverity DefaultSeverity => EctdValidationSeverity.Low;

        public IEnumerable<EctdValidationFinding> Evaluate(EctdValidationContext context) => findings;
    }
}
