using RATools.Application.Validation;
using RATools.Domain.Documents;

namespace RATools.Tests.Validation;

public sealed class LifecycleTargetResolverTests
{
    private static readonly Guid ApplicationId = Guid.NewGuid();

    [Fact]
    public void Resolve_MatchesExplicitTargetThatIsStillActive()
    {
        var (targetPlacement, targetDocument) = CreateHistorical("0000", "protocol.pdf");
        var (placement, document) = CreateCurrent("0001", "protocol.pdf", DocumentPlacementOperation.Replace, targetPlacement.Id);

        var resolution = LifecycleTargetResolver.Resolve(
            placement, document, [], [targetPlacement], Documents(targetDocument, document));

        Assert.Equal("MATCHED", resolution.ResultCode);
        Assert.Equal("ExplicitPlacementId", resolution.MatchStrategy);
        Assert.Equal("Active", resolution.HistoricalFinalState);
    }

    [Fact]
    public void Resolve_AutoMatchesUniqueHistoricalFileName()
    {
        // 未显式指定目标：同 section 同文件名唯一历史 → 自动解析。
        var (targetPlacement, targetDocument) = CreateHistorical("0000", "protocol.pdf");
        var (placement, document) = CreateCurrent("0001", "protocol.pdf", DocumentPlacementOperation.Replace, lifecycleTargetPlacementId: null);

        var resolution = LifecycleTargetResolver.Resolve(
            placement, document, [], [targetPlacement], Documents(targetDocument, document));

        Assert.Equal("MATCHED", resolution.ResultCode);
        Assert.Equal("SectionAndFileName", resolution.MatchStrategy);
        Assert.Equal(["ExplicitPlacementId", "SectionAndFileName"], resolution.AttemptedStrategies);
        Assert.Contains(targetPlacement.Id, resolution.HistoricalPlacementIds);
    }

    [Fact]
    public void Resolve_ReportsAmbiguousWhenMultipleHistoricalFileNamesMatch()
    {
        // 此前 LIFECYCLE_TARGET_AMBIGUOUS 是死分支（resolver 永不返回）；现在多解必须报出。
        var (firstTarget, firstDocument) = CreateHistorical("0000", "protocol.pdf");
        var (secondTarget, secondDocument) = CreateHistorical("0001", "protocol.pdf");
        var (placement, document) = CreateCurrent("0002", "protocol.pdf", DocumentPlacementOperation.Replace, lifecycleTargetPlacementId: null);

        var resolution = LifecycleTargetResolver.Resolve(
            placement, document, [], [firstTarget, secondTarget], Documents(firstDocument, secondDocument, document));

        Assert.Equal("LIFECYCLE_TARGET_AMBIGUOUS", resolution.ResultCode);
        Assert.Equal(2, resolution.HistoricalMatchCount);
        Assert.Equal("Ambiguous", resolution.HistoricalFinalState);
    }

    [Fact]
    public void Resolve_ReportsDeletedWhenTargetWasDeletedByLaterSequence()
    {
        // 链回放核心场景：replace 一个已被 delete 的 leaf 是官方验证器必拒的错误。
        var (originalPlacement, originalDocument) = CreateHistorical("0000", "protocol.pdf");
        var (deletePlacement, deleteDocument) = CreateHistorical("0001", "protocol.pdf", DocumentPlacementOperation.Delete, originalPlacement.Id);
        var (placement, document) = CreateCurrent("0002", "protocol.pdf", DocumentPlacementOperation.Replace, originalPlacement.Id);

        var resolution = LifecycleTargetResolver.Resolve(
            placement, document, [], [originalPlacement, deletePlacement], Documents(originalDocument, deleteDocument, document));

        Assert.Equal("LIFECYCLE_TARGET_DELETED", resolution.ResultCode);
        Assert.Equal("Deleted", resolution.HistoricalFinalState);
        Assert.Contains(deletePlacement.Id, resolution.HistoricalPlacementIds);
    }

    [Fact]
    public void Resolve_ReportsSupersededWhenTargetWasReplacedByLaterSequence()
    {
        var (originalPlacement, originalDocument) = CreateHistorical("0000", "protocol.pdf");
        var (replacePlacement, replaceDocument) = CreateHistorical("0001", "protocol-v2.pdf", DocumentPlacementOperation.Replace, originalPlacement.Id);
        var (placement, document) = CreateCurrent("0002", "protocol.pdf", DocumentPlacementOperation.Replace, originalPlacement.Id);

        var resolution = LifecycleTargetResolver.Resolve(
            placement, document, [], [originalPlacement, replacePlacement], Documents(originalDocument, replaceDocument, document));

        Assert.Equal("LIFECYCLE_TARGET_SUPERSEDED", resolution.ResultCode);
        Assert.Equal("Superseded", resolution.HistoricalFinalState);
    }

    [Fact]
    public void Resolve_ReportsNotFoundWhenNoStrategyMatches()
    {
        var (targetPlacement, targetDocument) = CreateHistorical("0000", "other-file.pdf");
        var (placement, document) = CreateCurrent("0001", "protocol.pdf", DocumentPlacementOperation.Delete, lifecycleTargetPlacementId: null);

        var resolution = LifecycleTargetResolver.Resolve(
            placement, document, [], [targetPlacement], Documents(targetDocument, document));

        Assert.Equal("DELETE_TARGET_NOT_FOUND", resolution.ResultCode);
        Assert.Equal(["ExplicitPlacementId", "SectionAndFileName"], resolution.AttemptedStrategies);
    }

    [Fact]
    public void Resolve_ReportsInvalidForExplicitTargetOutsideHistory()
    {
        var (placement, document) = CreateCurrent("0001", "protocol.pdf", DocumentPlacementOperation.Replace, Guid.NewGuid());

        var resolution = LifecycleTargetResolver.Resolve(
            placement, document, [], [], Documents(document));

        Assert.Equal("LIFECYCLE_TARGET_INVALID", resolution.ResultCode);
    }

    private static (DocumentPlacement Placement, SubmissionDocument Document) CreateHistorical(
        string sequenceNumber,
        string fileName,
        DocumentPlacementOperation operation = DocumentPlacementOperation.New,
        Guid? lifecycleTargetPlacementId = null)
    {
        var documentId = Guid.NewGuid();
        var document = SubmissionDocument.Rehydrate(
            documentId, fileName, "application/pdf", 1, "sha", "md5", $"C:/workspace/{sequenceNumber}/{fileName}", DateTime.UtcNow);
        var placement = DocumentPlacement.Rehydrate(
            Guid.NewGuid(), documentId, ApplicationId, sequenceNumber, "m1.1", operation, fileName, lifecycleTargetPlacementId, DateTime.UtcNow);
        return (placement, document);
    }

    private static (DocumentPlacement Placement, SubmissionDocument Document) CreateCurrent(
        string sequenceNumber,
        string fileName,
        DocumentPlacementOperation operation,
        Guid? lifecycleTargetPlacementId)
        => CreateHistorical(sequenceNumber, fileName, operation, lifecycleTargetPlacementId);

    private static IReadOnlyDictionary<Guid, SubmissionDocument> Documents(params SubmissionDocument[] documents)
        => documents.ToDictionary(x => x.Id, x => x);
}
