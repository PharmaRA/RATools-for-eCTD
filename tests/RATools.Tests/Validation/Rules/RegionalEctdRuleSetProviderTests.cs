using RATools.Application.Standards;
using RATools.Application.Validation.Rules;

namespace RATools.Tests.Validation.Rules;

public sealed class RegionalEctdRuleSetProviderTests
{
    [Theory]
    [InlineData("4.5")]
    [InlineData("4.7")]
    public void GetRuleSet_ReturnsRulesForFda322Criteria(string criteria)
    {
        var rule = new NoopRule();
        var provider = new RegionalEctdRuleSetProvider([rule]);

        var ruleSet = provider.GetRuleSet(CreateProfile("us-fda-ectd-3.2.2", criteria));

        Assert.Equal("us-fda-ectd-3.2.2", ruleSet.ProfileKey);
        Assert.Equal(criteria, ruleSet.ValidationCriteriaVersion);
        Assert.Same(rule, Assert.Single(ruleSet.Rules));
    }

    [Fact]
    public void GetRuleSet_ReturnsRulesForEu322CriteriaEu()
    {
        var rule = new NoopRule();
        var provider = new RegionalEctdRuleSetProvider([rule]);

        var ruleSet = provider.GetRuleSet(CreateProfile("eu-ectd-3.2.2", "EU"));

        Assert.Equal("eu-ectd-3.2.2", ruleSet.ProfileKey);
        Assert.Equal("EU", ruleSet.ValidationCriteriaVersion);
        Assert.Same(rule, Assert.Single(ruleSet.Rules));
    }

    [Fact]
    public void GetRuleSet_ThrowsForUnknownTemplateAndCriteriaCombination()
    {
        var provider = new RegionalEctdRuleSetProvider([new NoopRule()]);

        void Act() => provider.GetRuleSet(CreateProfile("ca-ectd-3.2.2", "CA"));

        var exception = Assert.Throws<EctdValidationRuleSetNotFoundException>(Act);

        Assert.Equal("ca-ectd-3.2.2", exception.TemplateKey);
        Assert.Equal("CA", exception.ValidationCriteriaVersion);
    }

    private static StandardsProfile CreateProfile(string templateKey, string criteriaVersion)
        => new(
            templateKey,
            templateKey,
            "Agency",
            "Region",
            "3.2.2",
            "M1",
            criteriaVersion,
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
