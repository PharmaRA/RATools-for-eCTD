namespace RATools.Application.Validation;

public interface IEctdWorkspacePathResolver
{
    EctdWorkspacePathResolution Resolve(string region, string ctdSection);
}

public sealed record EctdWorkspacePathResolution(
    string Region,
    string SectionPath,
    string ElementName,
    string RelativeFolderPath);
