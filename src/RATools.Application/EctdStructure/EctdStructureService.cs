using RATools.Application.EctdStructure.Dtos;
using RATools.Application.Validation;
using RATools.Application.Validation.Profiles;

namespace RATools.Application.EctdStructure;

public sealed class EctdStructureService : IEctdStructureService
{
    public EctdStructureDto Get(string region)
    {
        if (!string.Equals(region, "US", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Region '{region}' is not supported.", nameof(region));
        }

        var profile = SectionDictionaryProfiles.ResolveByRegion(region);
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
