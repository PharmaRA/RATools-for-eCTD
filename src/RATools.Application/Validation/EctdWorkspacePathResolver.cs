using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Validation.Profiles;

namespace RATools.Application.Validation;

public sealed class EctdWorkspacePathResolver : IEctdWorkspacePathResolver
{
    // 按模板 key 查表派发，而非硬编码单一模板：新增区域时在此注册其规范文件夹映射。
    // EU 是受控最小映射（仅 m1 顶层，与 EuRegionalXmlWriter 只接受 m1 leaves 一致）。
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, EctdWorkspacePathResolution>> FoldersByTemplateKey =
        new Dictionary<string, IReadOnlyDictionary<string, EctdWorkspacePathResolution>>(StringComparer.OrdinalIgnoreCase)
        {
            [EctdTemplateRegistry.DefaultTemplateKey] = FdaEctd322.CanonicalWorkspaceFolders,
            [EctdTemplateRegistry.EuTemplateKey] = EuEctd322.CanonicalWorkspaceFolders,
        };

    public EctdWorkspacePathResolution Resolve(string ectdTemplateKey, string ctdSection)
    {
        if (!FoldersByTemplateKey.TryGetValue(ectdTemplateKey, out var canonicalWorkspaceFolders))
        {
            throw new InvalidOperationException($"eCTD template '{ectdTemplateKey}' is not supported for workspace folder resolution.");
        }

        var normalizedSection = ctdSection.Trim();
        if (!canonicalWorkspaceFolders.TryGetValue(normalizedSection, out var resolution))
        {
            throw new InvalidOperationException($"Section '{ctdSection}' does not have a canonical workspace folder mapping.");
        }

        return resolution;
    }
}
