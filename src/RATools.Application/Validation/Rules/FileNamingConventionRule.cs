using System.Text.RegularExpressions;

namespace RATools.Application.Validation.Rules;

/// <summary>
/// FDA-NAMING-1：eCTD 文件名约定。文件名应只含小写字母、数字、连字符与点，
/// 不允许空格、大写或其他特殊字符；文件名加序列内相对路径总长度有上限。
/// 现有 EctdDocumentFileRules 只校验扩展名与路径分隔符，不覆盖大小写/字符集/长度。
/// </summary>
public sealed partial class FileNamingConventionRule : IEctdValidationRule
{
    // eCTD 历史上常见的相对路径长度上限（保守取 230，留出交付根前缀余量）。
    private const int MaxRelativePathLength = 230;

    // FDA 验证准则：文件名（含扩展名）不超过 64 字符。
    private const int MaxFileNameLength = 64;

    public string RuleId => "FDA-NAMING-1";

    public string Category => "FileNaming";

    public EctdValidationSeverity DefaultSeverity => EctdValidationSeverity.Medium;

    public IEnumerable<EctdValidationFinding> Evaluate(EctdValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Package is null)
        {
            yield break;
        }

        var leaves = context.Package.Module1Leaves.Concat(context.Package.IchBackboneLeaves);
        foreach (var leaf in leaves)
        {
            var fileName = leaf.FileName;
            if (!string.IsNullOrWhiteSpace(fileName) && !AllowedFileNamePattern().IsMatch(fileName))
            {
                yield return new EctdValidationFinding(
                    RuleId,
                    Category,
                    EctdValidationSeverity.Medium,
                    $"File name '{fileName}' contains characters outside the eCTD convention (lowercase letters, digits, hyphen and dot).",
                    "Rename the file to use only lowercase letters, digits, hyphens, and a single extension dot before publishing.",
                    leaf.CtdSection,
                    leaf.DocumentId,
                    leaf.PlacementId);
            }

            if (!string.IsNullOrWhiteSpace(fileName) && fileName.Length > MaxFileNameLength)
            {
                yield return new EctdValidationFinding(
                    RuleId,
                    Category,
                    EctdValidationSeverity.Medium,
                    $"File name '{fileName}' ({fileName.Length} chars) exceeds the {MaxFileNameLength}-character eCTD file-name limit.",
                    "Shorten the file name (including extension) to 64 characters or fewer before publishing.",
                    leaf.CtdSection,
                    leaf.DocumentId,
                    leaf.PlacementId);
            }

            if (!string.IsNullOrWhiteSpace(leaf.Href) && leaf.Href.Length > MaxRelativePathLength)
            {
                yield return new EctdValidationFinding(
                    RuleId,
                    Category,
                    EctdValidationSeverity.Medium,
                    $"Published path '{leaf.Href}' ({leaf.Href.Length} chars) exceeds the {MaxRelativePathLength}-character eCTD path-length limit.",
                    "Shorten folder or file names so the published relative path stays within the eCTD path-length limit.",
                    leaf.CtdSection,
                    leaf.DocumentId,
                    leaf.PlacementId);
            }
        }
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+)+$")]
    private static partial Regex AllowedFileNamePattern();
}
