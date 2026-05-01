namespace RATools.Application.Applications.Dtos;

public sealed record EctdTemplateDto(
    string Key,
    string DisplayName,
    string Region,
    string StandardName,
    string StandardVersion);
