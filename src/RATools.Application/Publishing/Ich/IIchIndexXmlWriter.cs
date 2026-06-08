using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.Ich;

public interface IIchIndexXmlWriter
{
    IchIndexXmlWriteResult Write(EctdSequencePackage package);
}
