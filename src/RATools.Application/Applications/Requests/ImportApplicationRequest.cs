namespace RATools.Application.Applications.Requests;

public sealed record ImportApplicationRequest(string WorkingDirectoryPath, string EctdTemplateKey, string SponsorName);
