using RATools.Application.Validation.Dtos;

namespace RATools.Application.Validation.Rules;

public interface IEctdValidationEngine
{
    IReadOnlyList<PublishReadinessFindingDto> Evaluate(EctdValidationContext context);
}

/// <summary>
/// 调度规则集、聚合 finding，并把 High/Medium 映射为 readiness 的 "Error"、
/// Low 映射为 "Warning"，从而复用 PublishReadinessService 的阻断语义与前端 finding 结构。
/// </summary>
public sealed class EctdValidationEngine(IEctdValidationRuleSetProvider ruleSetProvider) : IEctdValidationEngine
{
    public IReadOnlyList<PublishReadinessFindingDto> Evaluate(EctdValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ruleSet = ruleSetProvider.GetRuleSet(context.Profile);
        var findings = new List<PublishReadinessFindingDto>();

        foreach (var rule in ruleSet.Rules)
        {
            foreach (var finding in rule.Evaluate(context))
            {
                findings.Add(MapFinding(finding));
            }
        }

        return findings;
    }

    private static PublishReadinessFindingDto MapFinding(EctdValidationFinding finding)
    {
        var severity = finding.Severity == EctdValidationSeverity.Low ? "Warning" : "Error";
        return new PublishReadinessFindingDto(
            "ValidationCriteria",
            severity,
            finding.RuleId,
            finding.Message,
            finding.Category,
            finding.RecommendedAction,
            null,
            finding.SectionPath,
            finding.DocumentId,
            finding.PlacementId);
    }
}
