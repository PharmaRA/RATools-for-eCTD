using RATools.Application.Standards;

namespace RATools.Application.Validation.Rules;

/// <summary>
/// Routes shared eCTD validation rules across the standards profiles currently
/// supported by the multi-region architecture.
/// </summary>
public sealed class RegionalEctdRuleSetProvider : IEctdValidationRuleSetProvider
{
    private readonly IReadOnlyList<EctdValidationRuleSet> _ruleSets;

    public RegionalEctdRuleSetProvider(IEnumerable<IEctdValidationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var sharedRules = rules.ToArray();

        _ruleSets =
        [
            new EctdValidationRuleSet("us-fda-ectd-3.2.2", "4.5", sharedRules),
            new EctdValidationRuleSet("eu-ectd-3.2.2", "8.2", sharedRules),
            // Compatibility alias for persisted/test profiles created by the
            // pre-v8.2 controlled EU skeleton.
            new EctdValidationRuleSet("eu-ectd-3.2.2", "EU", sharedRules)
        ];
    }

    public EctdValidationRuleSet GetRuleSet(StandardsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var ruleSet in _ruleSets)
        {
            if (string.Equals(profile.TemplateKey, ruleSet.ProfileKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(profile.ValidationCriteriaVersion, ruleSet.ValidationCriteriaVersion, StringComparison.OrdinalIgnoreCase))
            {
                return ruleSet;
            }
        }

        throw new EctdValidationRuleSetNotFoundException(profile.TemplateKey, profile.ValidationCriteriaVersion);
    }
}
