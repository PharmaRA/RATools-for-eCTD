using RATools.Application.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

public sealed class PackageValidationOptions
{
    public const string SectionName = "PackageValidation";
    public bool Enabled { get; set; } = true;
    public string StorageRoot { get; set; } = "App_Data/package-validation";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public int MaxAttempts { get; set; } = 3;
    public PackageReadLimits ReadLimits { get; set; } = new();
}
