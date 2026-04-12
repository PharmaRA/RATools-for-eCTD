using RATools.Application.Validation.Profiles;

namespace RATools.Application.Validation;

public sealed class EctdWorkspacePathResolver : IEctdWorkspacePathResolver
{
    private static readonly IReadOnlyDictionary<string, EctdWorkspacePathResolution> CanonicalWorkspaceFolders = FdaEctd322.CanonicalWorkspaceFolders;

    public EctdWorkspacePathResolution Resolve(string region, string ctdSection)
    {
        if (!string.Equals(region, "US", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Region '{region}' is not supported for workspace folder resolution.");
        }

        var normalizedSection = ctdSection.Trim();
        if (!CanonicalWorkspaceFolders.TryGetValue(normalizedSection, out var resolution))
        {
            throw new InvalidOperationException($"Section '{ctdSection}' does not have a canonical workspace folder mapping.");
        }

        return resolution;
    }
}
