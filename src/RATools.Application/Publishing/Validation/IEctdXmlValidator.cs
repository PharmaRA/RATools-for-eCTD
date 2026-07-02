using RATools.Application.Abstractions.Publishing;
using RATools.Application.Standards;

namespace RATools.Application.Publishing.Validation;

public interface IEctdXmlValidator
{
    void Validate(BackboneGeneratedFile file, StandardsProfile? standardsProfile = null);
}
