namespace RATools.Application.Publishing.Regions;

public sealed class RegionalBackboneWriterRegistry(IEnumerable<IRegionalBackboneWriter> writers)
    : IRegionalBackboneWriterRegistry
{
    private readonly Dictionary<string, IRegionalBackboneWriter> _writers = writers.ToDictionary(
        writer => writer.RegionKey,
        writer => writer,
        StringComparer.OrdinalIgnoreCase);

    public IRegionalBackboneWriter Resolve(string regionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionKey);

        return _writers.TryGetValue(regionKey.Trim(), out var writer)
            ? writer
            : throw new RegionalBackboneWriterNotFoundException(regionKey);
    }
}
