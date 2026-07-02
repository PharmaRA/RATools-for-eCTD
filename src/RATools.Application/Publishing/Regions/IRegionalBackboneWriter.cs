using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.Regions;

public interface IRegionalBackboneWriter
{
    string RegionKey { get; }

    IReadOnlyList<BackboneGeneratedFile> WriteRegionalBackbones(EctdSequencePackage package);
}
