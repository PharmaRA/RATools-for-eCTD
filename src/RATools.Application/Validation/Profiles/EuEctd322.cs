using RATools.Application.Validation;

namespace RATools.Application.Validation.Profiles;

/// <summary>
/// EU 受控骨架的最小章节字典：只覆盖 Module 1 顶层节点（与 EuRegionalXmlWriter
/// 只接受 m1 leaves 的边界一致），供文档上传的规范文件夹解析与章节树展示使用。
/// 这不是官方 EU M1 完整章节集——与 eu-regional.dtd 占位符同级别的受控最小实现，
/// 扩展为完整 EU 支持时应从官方 EU M1 规范逐节补齐。
/// </summary>
public static class EuEctd322
{
    public const string ProfileName = "eu-ectd-3.2.2";

    public static readonly SectionDictionaryManualNode Root = Node(
        elementName: "ectd:ectd",
        sectionPath: string.Empty,
        title: "eCTD",
        folderName: null,
        children:
        [
            Node(
                "m1-eu-administrative-information",
                "m1",
                "Module 1 EU Administrative Information",
                "m1",
                [
                    Node("m1-0-cover-letter", "m1.0", "1.0 Cover Letter", "10-cover", []),
                    Node("m1-2-application-form", "m1.2", "1.2 Application Form", "12-form", []),
                    Node("m1-3-product-information", "m1.3", "1.3 Product Information", "13-pi", []),
                    Node("m1-4-information-about-the-experts", "m1.4", "1.4 Information About The Experts", "14-expert", []),
                    Node("m1-5-specific-requirements", "m1.5", "1.5 Specific Requirements For Different Types Of Applications", "15-specific", []),
                ]),
        ]);

    public static readonly IReadOnlyDictionary<string, EctdWorkspacePathResolution> CanonicalWorkspaceFolders =
        BuildCanonicalWorkspaceFolders();

    public static SectionDictionaryProfile ToProfile()
    {
        var nodes = Flatten(Root)
            .Where(x => !string.IsNullOrWhiteSpace(x.SectionPath))
            .ToArray();

        return new SectionDictionaryProfile
        {
            Name = ProfileName,
            ByElementName = nodes.ToDictionary(
                x => x.ElementName,
                x => new SectionDictionaryEntry(x.ElementName, x.SectionPath, ProfileName),
                StringComparer.OrdinalIgnoreCase),
            BySectionPath = nodes
                .GroupBy(x => x.SectionPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(n => new SectionDictionaryEntry(n.ElementName, n.SectionPath, ProfileName)).ToArray(),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static SectionDictionaryManualNode Node(
        string elementName,
        string sectionPath,
        string title,
        string? folderName,
        IReadOnlyCollection<SectionDictionaryManualNode> children)
        => new(elementName, sectionPath, title, children, folderName);

    private static IEnumerable<SectionDictionaryManualNode> Flatten(SectionDictionaryManualNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static Dictionary<string, EctdWorkspacePathResolution> BuildCanonicalWorkspaceFolders()
    {
        var folders = new Dictionary<string, EctdWorkspacePathResolution>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in Root.Children)
        {
            AddCanonicalWorkspaceFolders(module, [], folders);
        }

        return folders;
    }

    private static void AddCanonicalWorkspaceFolders(
        SectionDictionaryManualNode node,
        IReadOnlyList<SectionDictionaryManualNode> ancestors,
        Dictionary<string, EctdWorkspacePathResolution> folders)
    {
        if (string.IsNullOrWhiteSpace(node.FolderName))
        {
            throw new InvalidOperationException($"Section '{node.SectionPath}' is missing canonical folder metadata.");
        }

        var nodePath = ancestors.Concat([node]).ToArray();
        folders[node.SectionPath] = new EctdWorkspacePathResolution(
            "EU",
            node.SectionPath,
            node.ElementName,
            BuildRelativeFolderPath(nodePath));

        foreach (var child in node.Children)
        {
            AddCanonicalWorkspaceFolders(child, nodePath, folders);
        }
    }

    private static string BuildRelativeFolderPath(SectionDictionaryManualNode[] nodePath)
    {
        // EU M1 的物理布局锚定在 m1/eu 之下，与 EuRegionalXmlWriter 生成的
        // m1/eu/eu-regional.xml 相对 href 规则一致。
        return nodePath.Length == 1
            ? Path.Combine("m1", "eu")
            : Path.Combine("m1", "eu", nodePath[^1].FolderName!);
    }
}
