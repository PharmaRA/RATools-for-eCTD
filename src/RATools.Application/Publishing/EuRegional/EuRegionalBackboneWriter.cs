using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;

namespace RATools.Application.Publishing.EuRegional;

public sealed class EuRegionalBackboneWriter(IEuRegionalXmlWriter euRegionalXmlWriter) : IRegionalBackboneWriter
{
    public string RegionKey => "eu";

    public IReadOnlyList<BackboneGeneratedFile> WriteRegionalBackbones(EctdSequencePackage package)
    {
        var result = euRegionalXmlWriter.Write(package);
        return [new BackboneGeneratedFile(result.RelativePath, result.XmlContent)];
    }
}
