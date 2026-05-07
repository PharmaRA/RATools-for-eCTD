using RATools.Application.EctdStructure.Dtos;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Validation;
using RATools.Application.Validation.Profiles;

namespace RATools.Application.EctdStructure;

public sealed class EctdStructureService : IEctdStructureService
{
    public EctdStructureDto Get(string ectdTemplateKey)
    {
        if (!string.Equals(ectdTemplateKey, EctdTemplateRegistry.DefaultTemplateKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"eCTD template '{ectdTemplateKey}' is not supported.", nameof(ectdTemplateKey));
        }

        var profile = SectionDictionaryProfiles.ResolveByName(EctdTemplateRegistry.Default.ValidationProfileName);
        var roots = FdaEctd322.Root.Children.Select(x => MapNode(x, profile.Name)).ToArray();

        return new EctdStructureDto(profile.Name, "US", roots);
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
