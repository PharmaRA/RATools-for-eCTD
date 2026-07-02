namespace RATools.Application.Publishing.PackageModel;

public abstract class EctdPackageException(string message) : Exception(message);

public sealed class EctdPackageApplicationNotFoundException(Guid applicationId)
    : EctdPackageException($"Application {applicationId} was not found.")
{
    public Guid ApplicationId { get; } = applicationId;
}

public sealed class EctdPackageSequenceNotFoundException(Guid applicationId, string sequenceNumber)
    : EctdPackageException($"Sequence {sequenceNumber} does not exist on application {applicationId}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;
}

public sealed class EctdPackageStandardsProfileException(Guid applicationId, string sequenceNumber, string templateKey, string reason)
    : EctdPackageException($"Standards profile '{templateKey}' for sequence {sequenceNumber} is invalid: {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public string TemplateKey { get; } = templateKey;

    public string Reason { get; } = reason;
}

public sealed class EctdPackageDocumentNotFoundException(Guid applicationId, string sequenceNumber, Guid placementId, Guid documentId)
    : EctdPackageException($"Placement {placementId} in sequence {sequenceNumber} references missing document {documentId}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid PlacementId { get; } = placementId;

    public Guid DocumentId { get; } = documentId;
}

public sealed class EctdPackageUnsupportedOperationException(Guid applicationId, string sequenceNumber, Guid placementId, int operationValue)
    : EctdPackageException($"Placement {placementId} in sequence {sequenceNumber} has unsupported operation value {operationValue}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid PlacementId { get; } = placementId;

    public int OperationValue { get; } = operationValue;
}

public sealed class EctdPackageInvalidSectionException(Guid applicationId, string sequenceNumber, Guid placementId, string ctdSection)
    : EctdPackageException($"Placement {placementId} in sequence {sequenceNumber} has unsupported CTD section '{ctdSection}'.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid PlacementId { get; } = placementId;

    public string CtdSection { get; } = ctdSection;
}

public sealed class EctdPackageLifecycleTargetException(
    Guid applicationId,
    string sequenceNumber,
    Guid placementId,
    Guid? targetPlacementId,
    string reason)
    : EctdPackageException($"Placement {placementId} in sequence {sequenceNumber} requires a valid lifecycle target: {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid PlacementId { get; } = placementId;

    public Guid? TargetPlacementId { get; } = targetPlacementId;

    public string Reason { get; } = reason;
}
