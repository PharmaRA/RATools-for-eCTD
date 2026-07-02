namespace RATools.Application.Publishing.Regions;

public interface IRegionalBackboneWriterRegistry
{
    IRegionalBackboneWriter Resolve(string regionKey);
}
