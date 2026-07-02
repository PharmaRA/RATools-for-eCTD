using RATools.Application.Abstractions.Publishing;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;

namespace RATools.Tests.Publishing.Regions;

public sealed class RegionalBackboneWriterRegistryTests
{
    [Fact]
    public void Resolve_ReturnsUsWriterForUsRegion()
    {
        var writer = new StubRegionalBackboneWriter("us");
        var registry = new RegionalBackboneWriterRegistry([writer]);

        var resolved = registry.Resolve("US");

        Assert.Same(writer, resolved);
        Assert.Equal("us", resolved.RegionKey);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var writer = new StubRegionalBackboneWriter("us");
        var registry = new RegionalBackboneWriterRegistry([writer]);

        var resolved = registry.Resolve("uS");

        Assert.Same(writer, resolved);
    }

    [Fact]
    public void Resolve_ThrowsForUnsupportedRegion()
    {
        var registry = new RegionalBackboneWriterRegistry([new StubRegionalBackboneWriter("us")]);

        var exception = Assert.Throws<RegionalBackboneWriterNotFoundException>(() => registry.Resolve("EU"));

        Assert.Equal("EU", exception.RegionKey);
        Assert.Contains("EU", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddApplication_RegistersRegionalBackboneWriterRegistryWithUsWriter()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IRegionalBackboneWriterRegistry>();
        var writer = registry.Resolve("US");

        Assert.IsType<UsRegionalBackboneWriter>(writer);
        Assert.IsType<EuRegionalBackboneWriter>(registry.Resolve("EU"));
    }

    private sealed class StubRegionalBackboneWriter(string regionKey) : IRegionalBackboneWriter
    {
        public string RegionKey { get; } = regionKey;

        public IReadOnlyList<BackboneGeneratedFile> WriteRegionalBackbones(EctdSequencePackage package) => [];
    }
}
