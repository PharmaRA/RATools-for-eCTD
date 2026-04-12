namespace RATools.Application.EctdStructure.Dtos;

public sealed record EctdStructureDto(
    string ProfileName,
    string Region,
    IReadOnlyCollection<EctdStructureNodeDto> Roots);
