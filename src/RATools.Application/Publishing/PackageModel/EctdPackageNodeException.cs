namespace RATools.Application.Publishing.PackageModel;

public sealed class EctdPackageNodeException(string code, string message, Guid? nodeInstanceId = null,
    Guid? placementId = null, string? ctdSection = null) : EctdPackageException(message)
{
    public string Code { get; } = code;
    public Guid? NodeInstanceId { get; } = nodeInstanceId;
    public Guid? PlacementId { get; } = placementId;
    public string? CtdSection { get; } = ctdSection;
}
