using RATools.Application.Applications.EctdTemplates;

namespace RATools.Tests.Applications;

public sealed class EctdTemplateRegistryTests
{
    [Fact]
    public void Resolve_ReturnsDefaultUsTemplate()
    {
        var template = EctdTemplateRegistry.Resolve(EctdTemplateRegistry.DefaultTemplateKey);

        Assert.Equal("US", template.Region);
        Assert.Equal("US FDA eCTD 3.2.2", template.DisplayName);
    }

    [Fact]
    public void Resolve_ReturnsEuTemplate()
    {
        var template = EctdTemplateRegistry.Resolve("eu-ectd-3.2.2");

        Assert.Equal("eu-ectd-3.2.2", template.Key);
        Assert.Equal("EU eCTD 3.2.2", template.DisplayName);
        Assert.Equal("EU", template.Region);
        Assert.Equal("eCTD", template.StandardName);
        Assert.Equal("3.2.2", template.StandardVersion);
        Assert.Equal("eu-ectd-3.2.2", template.ValidationProfileName);
        Assert.Equal("EU M1", template.DtdVersion);
    }
}
