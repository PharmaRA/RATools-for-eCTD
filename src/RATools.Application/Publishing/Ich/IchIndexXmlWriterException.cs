namespace RATools.Application.Publishing.Ich;

public abstract class IchIndexXmlWriterException(string message) : Exception(message);

public sealed class IchIndexXmlSectionMappingException(
    Guid applicationId,
    string sequenceNumber,
    Guid? placementId,
    string? ctdSection,
    string reason)
    : IchIndexXmlWriterException($"Unable to map CTD section '{ctdSection ?? "(none)"}' in sequence {sequenceNumber}: {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid? PlacementId { get; } = placementId;

    public string? CtdSection { get; } = ctdSection;

    public string Reason { get; } = reason;
}
