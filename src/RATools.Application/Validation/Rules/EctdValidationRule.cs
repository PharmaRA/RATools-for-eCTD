using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Application.Validation.Requests;

namespace RATools.Application.Validation.Rules;

/// <summary>
/// eCTD 验证准则的严重级别。High/Medium 在映射到 readiness finding 时折叠为
/// "Error"（参与 isReady 阻断判定），Low 折叠为 "Warning"。
/// </summary>
public enum EctdValidationSeverity
{
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// 规则评估上下文，聚合规则所需的全部输入，避免每条规则各自访问仓储。
/// Package 在 dry-run 构建成功时存在。
/// </summary>
public sealed record EctdValidationContext(
    StandardsProfile Profile,
    ValidateSequenceRequest Request,
    EctdSequencePackage? Package,
    string? OutputRootPath);

/// <summary>
/// 引擎内部的 finding 表示，最终映射为 PublishReadinessFindingDto。
/// </summary>
public sealed record EctdValidationFinding(
    string RuleId,
    string Category,
    EctdValidationSeverity Severity,
    string Message,
    string RecommendedAction,
    string? SectionPath = null,
    Guid? DocumentId = null,
    Guid? PlacementId = null);

/// <summary>
/// 一条 eCTD 验证准则规则。规则应保持无状态；磁盘 I/O 通过上下文路径执行。
/// </summary>
public interface IEctdValidationRule
{
    string RuleId { get; }

    string Category { get; }

    EctdValidationSeverity DefaultSeverity { get; }

    IEnumerable<EctdValidationFinding> Evaluate(EctdValidationContext context);
}

/// <summary>
/// 规则集：一组规则加上其所属的 profile/version 标识。
/// </summary>
public sealed record EctdValidationRuleSet(
    string ProfileKey,
    string ValidationCriteriaVersion,
    IReadOnlyList<IEctdValidationRule> Rules);

public interface IEctdValidationRuleSetProvider
{
    EctdValidationRuleSet GetRuleSet(StandardsProfile profile);
}

public sealed class EctdValidationRuleSetNotFoundException(string templateKey, string validationCriteriaVersion)
    : Exception($"No eCTD validation rule set is registered for template '{templateKey}' and criteria version '{validationCriteriaVersion}'.")
{
    public string TemplateKey { get; } = templateKey;

    public string ValidationCriteriaVersion { get; } = validationCriteriaVersion;
}
