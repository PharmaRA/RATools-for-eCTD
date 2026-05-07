using RATools.Application.EctdStructure.Dtos;

namespace RATools.Application.EctdStructure;

public interface IEctdStructureService
{
    EctdStructureDto Get(string ectdTemplateKey);
}
