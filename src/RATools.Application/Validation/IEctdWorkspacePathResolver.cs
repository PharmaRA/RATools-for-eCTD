namespace RATools.Application.Validation;

public interface IEctdWorkspacePathResolver
{
    EctdWorkspacePathResolution Resolve(string ectdTemplateKey, string ctdSection);
}

public sealed record EctdWorkspacePathResolution(
    string Region,
    string SectionPath,
    string ElementName,
    string RelativeFolderPath);
