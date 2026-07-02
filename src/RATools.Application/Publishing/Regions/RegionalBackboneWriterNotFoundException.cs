namespace RATools.Application.Publishing.Regions;

public sealed class RegionalBackboneWriterNotFoundException(string regionKey)
    : Exception($"No regional eCTD backbone writer is registered for region '{regionKey}'.")
{
    public string RegionKey { get; } = regionKey;
}
