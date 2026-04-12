namespace RATools.Application.EctdStructure.Dtos;

public sealed record EctdStructureNodeDto(
    string ElementName,
    string SectionPath,
    string DisplayName,
    string SourceProfile,
    IReadOnlyCollection<EctdStructureNodeDto> Children);
