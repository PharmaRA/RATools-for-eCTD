namespace RATools.Application.Applications.EctdTemplates;

public sealed record EctdTemplateDefinition(
    string Key,
    string DisplayName,
    string Region,
    string StandardName,
    string StandardVersion,
    string ValidationProfileName,
    string DtdVersion);
