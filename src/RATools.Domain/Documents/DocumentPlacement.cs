using System.Xml;
using RATools.Domain.Common;
using RATools.Domain.Ctd;

namespace RATools.Domain.Documents;

public sealed class DocumentPlacement : Entity
{
    public DocumentPlacement(
        Guid documentId,
        Guid applicationId,
        string sequenceNumber,
        string ctdSection,
        DocumentPlacementOperation operation,
        string? title,
        string? leafId = null)
        : this(Guid.NewGuid(), documentId, applicationId, sequenceNumber, ctdSection, operation, title, null, DateTime.UtcNow, leafId)
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
        Guid? lifecycleTargetPlacementId,
        DateTime createdUtc,
        string? leafId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(ctdSection);

        Id = id;
        LeafId = string.IsNullOrWhiteSpace(leafId) ? $"leaf-{id:N}" : XmlConvert.VerifyName(leafId);
        DocumentId = documentId;
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber.Trim();
        CtdSection = ctdSection.Trim();
        Operation = operation;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        LifecycleTargetPlacementId = lifecycleTargetPlacementId;
        CreatedUtc = createdUtc;
    }

    public Guid DocumentId { get; private set; }

    public string LeafId { get; private set; }

    public Guid ApplicationId { get; private set; }

    public string SequenceNumber { get; private set; }

    public string CtdSection { get; private set; }

    public Guid? NodeInstanceId { get; private set; }

    public int SortOrder { get; private set; }

    public ImportedLeafSource? ImportedSource { get; private set; }

    public DocumentPlacementOperation Operation { get; private set; }

    public string? Title { get; private set; }

    public Guid? LifecycleTargetPlacementId { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public static DocumentPlacement Rehydrate(
        Guid id,
        Guid documentId,
        Guid applicationId,
        string sequenceNumber,
        string ctdSection,
        DocumentPlacementOperation operation,
        string? title,
        Guid? lifecycleTargetPlacementId,
        DateTime createdUtc,
        string? leafId = null,
        Guid? nodeInstanceId = null,
        int sortOrder = 0,
        ImportedLeafSource? importedSource = null)
    {
        if (nodeInstanceId == Guid.Empty) throw new ArgumentException("A node identity must be nonempty.", nameof(nodeInstanceId));
        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);
        return new DocumentPlacement(id, documentId, applicationId, sequenceNumber, ctdSection, operation, title, lifecycleTargetPlacementId, createdUtc, leafId)
        {
            NodeInstanceId = nodeInstanceId,
            SortOrder = sortOrder,
            ImportedSource = importedSource
        };
    }

    public void PreserveImportedSource(ImportedLeafSource source, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);
        _ = new LeafAddress(ApplicationId, SequenceNumber, source.BackboneRelativePath, LeafId);
        ImportedSource = source;
        SortOrder = sortOrder;
    }

    public void BindToNode(SequenceNode node, int sortOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);
        if (node.ApplicationId != ApplicationId || node.SequenceNumber != SequenceNumber)
            throw new CtdNodeConstraintException("PlacementNodeScopeMismatch", "A placement and its sequence node must have the same application and sequence.", node.NodeInstanceId);
        if (!node.AllowsLeaves)
            throw new CtdNodeConstraintException("NodeDoesNotAllowLeaves", "This node cannot receive document leaves.", node.NodeInstanceId);
        NodeInstanceId = node.NodeInstanceId;
        CtdSection = node.CtdSection;
        SortOrder = sortOrder;
    }

    public void ReassignSection(string ctdSection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ctdSection);
        if (NodeInstanceId is not null && ctdSection.Trim() != CtdSection)
            throw new CtdNodeConstraintException("NodeSelectionRequired", "Select a target instance when moving a node-bound placement.", NodeInstanceId);
        CtdSection = ctdSection.Trim();
    }

    public void ReviseTitle(string? title)
    {
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
    }

    public void ReviseOperation(DocumentPlacementOperation operation)
    {
        Operation = operation;
    }

    public void ReviseLifecycleTarget(Guid? lifecycleTargetPlacementId)
    {
        LifecycleTargetPlacementId = lifecycleTargetPlacementId;
    }
}
