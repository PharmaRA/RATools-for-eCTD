using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Standards;

namespace RATools.Tests.Standards;

public sealed class EuEctd322StandardsProfileProviderTests
{
    [Fact]
    public void GetProfile_ExposesPinnedSourceLifecycleWithoutActivatingSkeleton()
    {
        var profile = new EuEctd322StandardsProfileProvider().GetProfile(EctdTemplateRegistry.EuTemplateKey);

        Assert.NotNull(profile.Lifecycle);
        Assert.Equal("3.1.1", profile.Lifecycle!.Version);
        Assert.Equal(new DateOnly(2025, 12, 1), profile.Lifecycle.EffectiveFrom);
        Assert.Null(profile.Lifecycle.RetiredFrom);
        Assert.Equal(StandardsLifecycleStatus.AcquiredNotActive, profile.Lifecycle.Status);
        Assert.True(profile.Lifecycle.IsEffectiveOn(new DateOnly(2026, 8, 19)));
        Assert.Contains("esubmission.ema.europa.eu/eumodule1", profile.Lifecycle.SourceUrl, StringComparison.Ordinal);
        Assert.Contains("never silently rewrite", profile.Lifecycle.RetirementPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardsLifecycle_StopsBeingEffectiveAtRetirementDate()
    {
        var lifecycle = new StandardsLifecycle(
            "https://example.test/rules",
            "1.0",
            new DateOnly(2024, 1, 1),
            new DateOnly(2025, 1, 1),
            StandardsLifecycleStatus.Retired,
            "Keep historical snapshots.");

        Assert.True(lifecycle.IsEffectiveOn(new DateOnly(2024, 12, 31)));
        Assert.False(lifecycle.IsEffectiveOn(new DateOnly(2025, 1, 1)));
    }
}
