using RATools.Application.Abstractions.Publishing;

namespace RATools.Application.Publishing.Validation;

public interface IEctdXmlValidator
{
    void Validate(BackboneGeneratedFile file);
}
