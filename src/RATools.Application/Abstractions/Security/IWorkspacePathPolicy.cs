namespace RATools.Application.Abstractions.Security;

public interface IWorkspacePathPolicy
{
    IReadOnlyCollection<string> GetAllowedRoots();

    string EnsureAllowed(string path);
}
