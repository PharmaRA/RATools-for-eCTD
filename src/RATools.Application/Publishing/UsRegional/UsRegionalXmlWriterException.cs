namespace RATools.Application.Publishing.UsRegional;

public abstract class UsRegionalXmlWriterException(string message) : Exception(message);

public sealed class UsRegionalXmlMetadataException(
    Guid applicationId,
    string sequenceNumber,
    string fieldName,
    string reason)
    : UsRegionalXmlWriterException($"Unable to generate US regional XML for sequence {sequenceNumber}: metadata field '{fieldName}' {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public string FieldName { get; } = fieldName;

    public string Reason { get; } = reason;
}

public sealed class UsRegionalXmlSectionMappingException(
    Guid applicationId,
    string sequenceNumber,
    Guid? placementId,
    string? ctdSection,
    string reason)
    : UsRegionalXmlWriterException($"Unable to map Module 1 section '{ctdSection ?? "(none)"}' in sequence {sequenceNumber}: {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid? PlacementId { get; } = placementId;

    public string? CtdSection { get; } = ctdSection;

    public string Reason { get; } = reason;
}
