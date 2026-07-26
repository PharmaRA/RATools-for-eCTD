namespace RATools.Application.Validation;

/// <summary>
/// 序列验证 issue code 的单一元数据注册表：readiness 分类与建议措施都从这里取。
/// 此前 PublishReadinessService.MapValidationFinding 手工维护两个 switch，
/// 与 SequenceValidationService 实际发出的 code 集已经漂移（缺 MISSING_LEAF_CORE_METADATA、
/// SEQUENCE_NOT_LATEST、DUPLICATE_PLACEMENT 等，全部落到默认 "Validation" 分类）。
/// 新增 code 时在此登记一行；未登记的 code 使用显式的 fallback 条目。
/// </summary>
public static class ValidationRuleCatalog
{
    public sealed record ValidationRuleMetadata(string Category, string RecommendedAction);

    public static readonly ValidationRuleMetadata Fallback = new(
        "Validation",
        "Resolve the validation issue before publishing.");

    private static readonly IReadOnlyDictionary<string, ValidationRuleMetadata> Entries =
        new Dictionary<string, ValidationRuleMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            // 应用/序列层
            ["APP_NOT_FOUND"] = new("SequenceContent", "Verify the application id and reload before publishing."),
            ["SEQ_NOT_FOUND"] = new("SequenceContent", "Create the sequence or correct the sequence number before publishing."),
            ["SEQUENCE_NOT_LATEST"] = new("SequenceContent", "Publish the latest sequence, or confirm intentionally republishing an earlier one."),
            ["SEQUENCE_NUMBER_FORMAT_INVALID"] = new("SequenceContent", "Use a four-digit numeric sequence number (for example 0000)."),
            ["SEQUENCE_GAP_DETECTED"] = new("SequenceContent", "Confirm the sequence numbering gap is intentional before submission."),
            ["NO_PLACEMENTS"] = new("SequenceContent", "Add at least one document placement to the sequence before publishing."),

            // 文档清单
            ["FILE_MISSING"] = new("DocumentInventory", "Restore the missing file on disk or update the document storage path before publishing."),
            ["DOCUMENT_NOT_FOUND"] = new("DocumentInventory", "Restore the missing document record or remove the broken placement before publishing."),
            ["DUPLICATE_PUBLISHED_DOCUMENT_PATH"] = new("DocumentInventory", "Rename or relocate documents so each published path is unique before publishing."),
            ["DUPLICATE_PLACEMENT"] = new("DocumentInventory", "Remove the duplicate placement so each document appears once per section."),
            ["MISSING_LEAF_CORE_METADATA"] = new("DocumentInventory", "Re-upload the document so file name, media type, and checksum are recorded."),

            // 章节映射
            ["INVALID_SECTION_PATH"] = new("SectionMapping", "Correct the CTD section path so it matches the supported standards profile before publishing."),
            ["SECTION_MISSING"] = new("SectionMapping", "Assign a CTD section to the placement before publishing."),
            ["SECTION_DEPTH_SHALLOW"] = new("SectionMapping", "Consider placing the document at a deeper, more specific CTD node."),
            ["NON_STANDARD_SECTION_PATTERN"] = new("SectionMapping", "Confirm the non-standard section path is intentional before publishing."),
            ["TITLE_FALLBACK_USED"] = new("SectionMapping", "Provide an explicit leaf title instead of relying on the file name."),
            ["MEDIA_TYPE_MISMATCH"] = new("DocumentInventory", "Align the stored media type with the file extension before publishing."),

            // 生命周期
            ["REPLACE_TARGET_NOT_FOUND"] = new("Lifecycle", "Select a valid historical replace target before publishing."),
            ["DELETE_TARGET_NOT_FOUND"] = new("Lifecycle", "Select a valid historical delete target before publishing."),
            ["APPEND_TARGET_NOT_FOUND"] = new("Lifecycle", "Select a valid historical append target before publishing."),
            ["LIFECYCLE_TARGET_INVALID"] = new("Lifecycle", "Select a valid historical lifecycle target in the same section before publishing."),
            ["LIFECYCLE_TARGET_AMBIGUOUS"] = new("Lifecycle", "Multiple historical targets match; select the target placement explicitly."),
            ["LIFECYCLE_TARGET_SUPERSEDED"] = new("Lifecycle", "The target was already replaced by a later sequence; target the latest active leaf."),
            ["LIFECYCLE_TARGET_DELETED"] = new("Lifecycle", "The target was already deleted by a later sequence and cannot be modified."),
            ["LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE"] = new("Lifecycle", "Lifecycle targets must reference an earlier sequence, not the current one."),
            ["UNSUPPORTED_OPERATION_VALUE"] = new("Lifecycle", "Change the placement operation to a supported eCTD lifecycle action before publishing."),
        };

    public static ValidationRuleMetadata Resolve(string code)
        => Entries.TryGetValue(code, out var metadata) ? metadata : Fallback;

    public static IReadOnlyCollection<string> KnownCodes => Entries.Keys.ToArray();
}
