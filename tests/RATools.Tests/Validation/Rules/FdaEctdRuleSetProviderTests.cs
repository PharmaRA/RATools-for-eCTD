using RATools.Application.Standards;
using RATools.Application.Validation.Rules;

namespace RATools.Tests.Validation.Rules;

public sealed class FdaEctdRuleSetProviderTests
{
    [Fact]
    public void GetRuleSet_ReturnsFdaRulesForFda322Criteria45()
    {
        var rule = new NoopRule();
        var provider = new FdaEctdRuleSetProvider([rule]);

        var ruleSet = provider.GetRuleSet(CreateProfile("us-fda-ectd-3.2.2", "4.5"));

        Assert.Equal("us-fda-ectd-3.2.2", ruleSet.ProfileKey);
        Assert.Equal("4.5", ruleSet.ValidationCriteriaVersion);
        Assert.Same(rule, Assert.Single(ruleSet.Rules));
    }

    [Fact]
    public void GetRuleSet_ThrowsForUnknownProfile()
    {
        var provider = new FdaEctdRuleSetProvider([new NoopRule()]);

        var exception = Assert.Throws<EctdValidationRuleSetNotFoundException>(
            () => provider.GetRuleSet(CreateProfile("eu-ectd-3.2.2", "4.5")));

        Assert.Equal("eu-ectd-3.2.2", exception.TemplateKey);
        Assert.Equal("4.5", exception.ValidationCriteriaVersion);
    }

    [Fact]
    public void GetRuleSet_ThrowsForUnknownCriteriaVersion()
    {
        var provider = new FdaEctdRuleSetProvider([new NoopRule()]);

        var exception = Assert.Throws<EctdValidationRuleSetNotFoundException>(
            () => provider.GetRuleSet(CreateProfile("us-fda-ectd-3.2.2", "5.0")));

        Assert.Equal("us-fda-ectd-3.2.2", exception.TemplateKey);
        Assert.Equal("5.0", exception.ValidationCriteriaVersion);
    }

    private static StandardsProfile CreateProfile(string templateKey, string criteriaVersion)
        => new(
            templateKey,
            templateKey,
            "FDA",
            "United States",
            "3.2.2",
            "3.3",
            "1.9",
            criteriaVersion,
            [],
            []);

    private sealed class NoopRule : IEctdValidationRule
    {
        public string RuleId => "NOOP";

        public string Category => "Noop";

        public EctdValidationSeverity DefaultSeverity => EctdValidationSeverity.Low;

        public IEnumerable<EctdValidationFinding> Evaluate(EctdValidationContext context) => [];
    }
}
