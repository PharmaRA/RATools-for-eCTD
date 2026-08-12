namespace RATools.Infrastructure.Security;

public sealed class DeploymentOptions
{
    public const string SectionName = "Deployment";
    public const string LocalOnlyMode = "LocalOnly";

    public string Mode { get; set; } = string.Empty;

    public bool Containerized { get; set; }

    public string InstanceLockPath { get; set; } = "App_Data/ratools-api.lock";
}
