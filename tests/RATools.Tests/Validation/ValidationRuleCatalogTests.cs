using System.Text.RegularExpressions;
using RATools.Application.Validation;

namespace RATools.Tests.Validation;

public sealed partial class ValidationRuleCatalogTests
{
    [GeneratedRegex("\"([A-Z][A-Z0-9_]+)\"")]
    private static partial Regex IssueCodeLiteralRegex();

    [Fact]
    public void Resolve_ReturnsLifecycleMetadataForLifecycleCodes()
    {
        var metadata = ValidationRuleCatalog.Resolve("LIFECYCLE_TARGET_DELETED");

        Assert.Equal("Lifecycle", metadata.Category);
        Assert.Contains("deleted", metadata.RecommendedAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_FallsBackForUnknownCode()
    {
        var metadata = ValidationRuleCatalog.Resolve("SOME_FUTURE_CODE");

        Assert.Same(ValidationRuleCatalog.Fallback, metadata);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal(
            ValidationRuleCatalog.Resolve("FILE_MISSING"),
            ValidationRuleCatalog.Resolve("file_missing"));
    }

    /// <summary>
    /// 漂移守卫：SequenceValidationService（含 LifecycleTargetResolver）源码中出现的
    /// 每个 issue code 字面量都必须在 catalog 登记。此前 readiness 的映射与实际
    /// code 集脱节正是因为没有这道闸门。
    /// </summary>
    [Fact]
    public void EveryEmittedIssueCodeIsRegisteredInCatalog()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var sourceFiles = new[]
        {
            Path.Combine(repositoryRoot, "src", "RATools.Application", "Validation", "SequenceValidationService.cs"),
            Path.Combine(repositoryRoot, "src", "RATools.Application", "Validation", "LifecycleTargetResolver.cs"),
        };

        var knownCodes = ValidationRuleCatalog.KnownCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // MATCHED 是 lifecycle 成功结果码，不是 issue；排除之。
        var nonIssueCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MATCHED" };

        var unregistered = new List<string>();
        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            foreach (Match match in IssueCodeLiteralRegex().Matches(source))
            {
                var code = match.Groups[1].Value;
                // 至少含一个下划线的全大写字面量才视为 issue code（排除 "US"、"EU" 等）。
                if (!code.Contains('_') || nonIssueCodes.Contains(code))
                {
                    continue;
                }

                if (!knownCodes.Contains(code))
                {
                    unregistered.Add($"{Path.GetFileName(sourceFile)}: {code}");
                }
            }
        }

        Assert.Empty(unregistered);
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
