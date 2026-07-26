using RATools.Application.EctdStructure.Dtos;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Validation;
using RATools.Application.Validation.Profiles;

namespace RATools.Application.EctdStructure;

public sealed class EctdStructureService : IEctdStructureService
{
    // 与 EctdWorkspacePathResolver 相同的查表派发：模板 key → (章节树根, 区域, profile 名)。
    private static readonly IReadOnlyDictionary<string, (SectionDictionaryManualNode Root, string Region, string ProfileName)> StructuresByTemplateKey =
        new Dictionary<string, (SectionDictionaryManualNode, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            [EctdTemplateRegistry.DefaultTemplateKey] = (
                FdaEctd322.Root,
                "US",
                SectionDictionaryProfiles.ResolveByName(EctdTemplateRegistry.Default.ValidationProfileName).Name),
            [EctdTemplateRegistry.EuTemplateKey] = (
                EuEctd322.Root,
                "EU",
                EuEctd322.ProfileName),
        };

    public EctdStructureDto Get(string ectdTemplateKey)
    {
        if (!StructuresByTemplateKey.TryGetValue(ectdTemplateKey, out var structure))
        {
            throw new ArgumentException($"eCTD template '{ectdTemplateKey}' is not supported.", nameof(ectdTemplateKey));
        }

        var roots = structure.Root.Children.Select(x => MapNode(x, structure.ProfileName)).ToArray();

        return new EctdStructureDto(structure.ProfileName, structure.Region, roots);
    }

    private static EctdStructureNodeDto MapNode(SectionDictionaryManualNode node, string sourceProfile)
    {
        return new EctdStructureNodeDto(
            node.ElementName,
            node.SectionPath,
            node.Title,
            sourceProfile,
            node.Children.Select(x => MapNode(x, sourceProfile)).OrderBy(x => x.SectionPath, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
