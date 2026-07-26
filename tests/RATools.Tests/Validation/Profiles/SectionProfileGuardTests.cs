using RATools.Application.Validation;
using RATools.Application.Validation.Profiles;

namespace RATools.Tests.Validation.Profiles;

/// <summary>
/// 章节字典 profile 守护测试。docs/section-dictionary/us-manual-profile-maintenance.md
/// 曾引用五个不存在的测试类作为"合并前必跑清单"——filter 匹配 0 个测试也会静默通过，
/// 是一道假门禁。本类是真实的守护：结构完整性 + 关键 section 存在 + 元数据不变量。
/// </summary>
public sealed class SectionProfileGuardTests
{
    [Fact]
    public void FdaProfile_EveryNodeWithSectionPathHasCompleteMetadata()
    {
        var violations = new List<string>();
        VisitNodes(FdaEctd322.Root, node =>
        {
            if (string.IsNullOrWhiteSpace(node.SectionPath))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.ElementName))
            {
                violations.Add($"{node.SectionPath}: missing ElementName");
            }

            if (string.IsNullOrWhiteSpace(node.Title))
            {
                violations.Add($"{node.SectionPath}: missing Title");
            }

            if (string.IsNullOrWhiteSpace(node.FolderName))
            {
                violations.Add($"{node.SectionPath}: missing FolderName");
            }
        });

        Assert.Empty(violations);
    }

    [Fact]
    public void FdaProfile_ContainsAllFiveCtdModules()
    {
        var moduleSectionPaths = FdaEctd322.Root.Children.Select(x => x.SectionPath).ToArray();

        Assert.Equal(["m1", "m2", "m3", "m4", "m5"], moduleSectionPaths);
    }

    [Theory]
    [InlineData("m1.1")]
    [InlineData("m1.2")]
    [InlineData("m2.3")]
    [InlineData("m2.7")]
    [InlineData("m3.2")]
    [InlineData("m4.2")]
    [InlineData("m5.3")]
    public void FdaProfile_ContainsCoreSubmissionSections(string sectionPath)
    {
        Assert.True(
            FdaEctd322.CanonicalWorkspaceFolders.ContainsKey(sectionPath),
            $"Core CTD section '{sectionPath}' is missing from the FDA canonical folder map.");
    }

    [Fact]
    public void FdaProfile_SectionPathsAreUniquePerElementName()
    {
        var elementNames = new List<string>();
        VisitNodes(FdaEctd322.Root, node =>
        {
            if (!string.IsNullOrWhiteSpace(node.ElementName))
            {
                elementNames.Add(node.ElementName);
            }
        });

        var duplicates = elementNames
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void FdaProfile_ToProfileExposesEveryPathedNode()
    {
        var pathedNodeCount = 0;
        VisitNodes(FdaEctd322.Root, node =>
        {
            if (!string.IsNullOrWhiteSpace(node.SectionPath))
            {
                pathedNodeCount += 1;
            }
        });

        var profile = SectionDictionaryProfiles.FdaEctd32;
        Assert.Equal(pathedNodeCount, profile.ByElementName.Count);
    }

    [Fact]
    public void FdaProfile_CanonicalFoldersAnchorModule1UnderUs()
    {
        var m1 = FdaEctd322.CanonicalWorkspaceFolders["m1"];
        Assert.Equal(Path.Combine("m1", "us"), m1.RelativeFolderPath);
        Assert.Equal("US", m1.Region);
    }

    [Fact]
    public void EuProfile_EveryNodeWithSectionPathHasCompleteMetadata()
    {
        var violations = new List<string>();
        VisitNodes(EuEctd322.Root, node =>
        {
            if (string.IsNullOrWhiteSpace(node.SectionPath))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.ElementName)
                || string.IsNullOrWhiteSpace(node.Title)
                || string.IsNullOrWhiteSpace(node.FolderName))
            {
                violations.Add(node.SectionPath);
            }
        });

        Assert.Empty(violations);
    }

    [Fact]
    public void EuProfile_CanonicalFoldersAnchorModule1UnderEu()
    {
        var m1 = EuEctd322.CanonicalWorkspaceFolders["m1"];
        Assert.Equal(Path.Combine("m1", "eu"), m1.RelativeFolderPath);
        Assert.Equal("EU", m1.Region);
    }

    private static void VisitNodes(SectionDictionaryManualNode node, Action<SectionDictionaryManualNode> visit)
    {
        visit(node);
        foreach (var child in node.Children)
        {
            VisitNodes(child, visit);
        }
    }
}
