namespace RATools.Infrastructure.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string ApiKey { get; set; } = string.Empty;

    public string[] AllowedWorkspaceRoots { get; set; } = [];
}
