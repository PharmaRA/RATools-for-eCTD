namespace RATools.Application.Applications.Requests;

public sealed record CreateApplicationRequest(string ApplicationNumber, string EctdTemplateKey, string SponsorName, string WorkingDirectoryParentPath);
