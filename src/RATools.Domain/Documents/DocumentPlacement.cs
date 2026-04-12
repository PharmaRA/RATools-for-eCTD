using RATools.Domain.Common;

namespace RATools.Domain.Documents;

public sealed class DocumentPlacement : Entity
{
    public DocumentPlacement(
        Guid documentId,
        Guid applicationId,
        string sequenceNumber,
        string ctdSection,
        DocumentPlacementOperation operation,
        string? title)
        : this(Guid.NewGuid(), documentId, applicationId, sequenceNumber, ctdSection, operation, title, DateTime.UtcNow)
    {
    }

    private DocumentPlacement(
        Guid id,
        Guid documentId,
        Guid applicationId,
        string sequenceNumber,
        string ctdSection,
        DocumentPlacementOperation operation,
        string? title,
        DateTime createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(ctdSection);

        Id = id;
        DocumentId = documentId;
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber.Trim();
        CtdSection = ctdSection.Trim();
        Operation = operation;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        CreatedUtc = createdUtc;
    }

    public Guid DocumentId { get; private set; }

    public Guid ApplicationId { get; private set; }

    public string SequenceNumber { get; private set; }

    public string CtdSection { get; private set; }

    public DocumentPlacementOperation Operation { get; private set; }

    public string? Title { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public static DocumentPlacement Rehydrate(
        Guid id,
        Guid documentId,
        Guid applicationId,
        string sequenceNumber,
        string ctdSection,
        DocumentPlacementOperation operation,
        string? title,
        DateTime createdUtc)
    {
        return new DocumentPlacement(id, documentId, applicationId, sequenceNumber, ctdSection, operation, title, createdUtc);
    }

    public void ReassignSection(string ctdSection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ctdSection);
        CtdSection = ctdSection.Trim();
    }
}
