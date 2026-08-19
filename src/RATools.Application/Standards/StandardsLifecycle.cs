namespace RATools.Application.Standards;

public enum StandardsLifecycleStatus
{
    Active,
    AcquiredNotActive,
    Retired
}

public sealed record StandardsLifecycle(
    string SourceUrl,
    string Version,
    DateOnly EffectiveFrom,
    DateOnly? RetiredFrom,
    StandardsLifecycleStatus Status,
    string RetirementPolicy)
{
    public bool IsEffectiveOn(DateOnly date)
        => date >= EffectiveFrom
           && (RetiredFrom is null || date < RetiredFrom.Value);
}
