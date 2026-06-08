using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.UsRegional;

public interface IUsRegionalXmlWriter
{
    UsRegionalXmlWriteResult Write(EctdSequencePackage package);
}
