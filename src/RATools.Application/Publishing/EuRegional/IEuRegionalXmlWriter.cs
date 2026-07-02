using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.EuRegional;

public interface IEuRegionalXmlWriter
{
    EuRegionalXmlWriteResult Write(EctdSequencePackage package);
}
