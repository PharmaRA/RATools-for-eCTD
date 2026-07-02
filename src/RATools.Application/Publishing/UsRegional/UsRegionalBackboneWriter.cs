using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;

namespace RATools.Application.Publishing.UsRegional;

public sealed class UsRegionalBackboneWriter(IUsRegionalXmlWriter usRegionalXmlWriter) : IRegionalBackboneWriter
{
    public string RegionKey => "us";

    public IReadOnlyList<BackboneGeneratedFile> WriteRegionalBackbones(EctdSequencePackage package)
    {
        var result = usRegionalXmlWriter.Write(package);
        return [new BackboneGeneratedFile(result.RelativePath, result.XmlContent)];
    }
}
