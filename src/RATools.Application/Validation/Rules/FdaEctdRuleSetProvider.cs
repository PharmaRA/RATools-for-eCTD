using RATools.Application.Standards;

namespace RATools.Application.Validation.Rules;

/// <summary>
/// 以 (TemplateKey, ValidationCriteriaVersion) 为键选择规则集。第一版仅注册
/// FDA 规则集，键为 (us-fda-ectd-3.2.2, 4.5)，与 FdaEctd322StandardsProfileProvider
/// 返回的 profile 对齐。未知组合 fail-fast。
/// </summary>
public sealed class FdaEctdRuleSetProvider : IEctdValidationRuleSetProvider
{
    private readonly EctdValidationRuleSet _fdaRuleSet;

    public FdaEctdRuleSetProvider(IEnumerable<IEctdValidationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _fdaRuleSet = new EctdValidationRuleSet(
            "us-fda-ectd-3.2.2",
            "4.5",
            rules.ToArray());
    }

    public EctdValidationRuleSet GetRuleSet(StandardsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.Equals(profile.TemplateKey, _fdaRuleSet.ProfileKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.ValidationCriteriaVersion, _fdaRuleSet.ValidationCriteriaVersion, StringComparison.OrdinalIgnoreCase))
        {
            return _fdaRuleSet;
        }

        throw new EctdValidationRuleSetNotFoundException(profile.TemplateKey, profile.ValidationCriteriaVersion);
    }
}
