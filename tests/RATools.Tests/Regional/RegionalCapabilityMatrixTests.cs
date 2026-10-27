using RATools.Application.PackageValidation;
using RATools.Application.Regional;
using RATools.Application.Standards;

namespace RATools.Tests.Regional;

public sealed class RegionalCapabilityMatrixTests
{
    [Fact]
    public void NoShippedProfileClaimsFinalizationAndEuIsMarkedAcquiredNotActive()
    {
        var matrix = RegionalCapabilityMatrix.Build(new CompositeStandardsProfileProvider(
            [new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]),
            PackageValidationCatalog.Current);
        Assert.NotEmpty(matrix);
        // Every shipped catalog profile is unqualified; the matrix must not upgrade any
        // of them to formal delivery support.
        Assert.All(matrix, capability =>
        {
            Assert.Equal(RegionalCapabilityState.Blocked, capability.Finalize);
            Assert.Equal("NotEvaluated", capability.EvidenceStatus);
            Assert.NotEmpty(capability.Limitations);
            Assert.Equal("NotEvaluated", capability.ApplicationType);
            Assert.Equal("NotEvaluated", capability.ProcedureType);
        });
        var eu = Assert.Single(matrix, capability => capability.Region == "EU");
        Assert.Equal("AcquiredNotActive", eu.StandardsStatus);
        Assert.Contains(eu.Limitations, reason => reason.Contains("acquired but not active", StringComparison.Ordinal));
        var us = Assert.Single(matrix, capability => capability.Region == "US");
        Assert.Contains("US", us.SectionScope, StringComparison.Ordinal);
        Assert.DoesNotContain(us.Limitations, reason => reason.Contains("acquired but not active", StringComparison.Ordinal));
    }

    [Fact]
    public void ICHScopeDoesNotInheritARegionalModuleOneClaim()
    {
        var matrix = RegionalCapabilityMatrix.Build(new CompositeStandardsProfileProvider(
            [new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]),
            PackageValidationCatalog.Current);
        var ich = Assert.Single(matrix, capability => capability.Region == "ICH");
        Assert.Equal("ICH M2-M5", ich.SectionScope);
        // The independent ICH inspection profile has no publishing standards provider.
        Assert.Equal(RegionalCapabilityState.NotImplemented, ich.CreateDraft);
        Assert.Equal(RegionalCapabilityState.NotImplemented, ich.TrialPublish);
        Assert.Equal(RegionalCapabilityState.Blocked, ich.Finalize);
        Assert.Contains(ich.Limitations, reason => reason.Contains("could not be resolved", StringComparison.Ordinal));
    }
}
