using RATools.Domain.Documents;

namespace RATools.Tests.Domain;

public sealed class DocumentPlacementTests
{
    [Fact]
    public void Constructor_TrimsAndNormalizesTitle()
    {
        var placement = new DocumentPlacement(
            Guid.NewGuid(), Guid.NewGuid(), " 0000 ", " m1.1 ", DocumentPlacementOperation.New, "  ");

        Assert.Equal("0000", placement.SequenceNumber);
        Assert.Equal("m1.1", placement.CtdSection);
        // 空白标题归一化为 null（backbone 回退用文件名）。
        Assert.Null(placement.Title);

        Assert.Throws<ArgumentException>(() => new DocumentPlacement(
            Guid.NewGuid(), Guid.NewGuid(), " ", "m1.1", DocumentPlacementOperation.New, null));
        Assert.Throws<ArgumentException>(() => new DocumentPlacement(
            Guid.NewGuid(), Guid.NewGuid(), "0000", "", DocumentPlacementOperation.New, null));
    }

    [Fact]
    public void ReassignSection_UpdatesSectionAndRejectsBlank()
    {
        var placement = CreatePlacement();

        placement.ReassignSection(" m1.2 ");

        Assert.Equal("m1.2", placement.CtdSection);
        Assert.Throws<ArgumentException>(() => placement.ReassignSection(" "));
    }

    [Fact]
    public void ReviseTitleOperationAndLifecycleTarget_UpdateState()
    {
        var placement = CreatePlacement();
        var targetId = Guid.NewGuid();

        placement.ReviseTitle(" Cover Letter ");
        placement.ReviseOperation(DocumentPlacementOperation.Replace);
        placement.ReviseLifecycleTarget(targetId);

        Assert.Equal("Cover Letter", placement.Title);
        Assert.Equal(DocumentPlacementOperation.Replace, placement.Operation);
        Assert.Equal(targetId, placement.LifecycleTargetPlacementId);

        placement.ReviseTitle(null);
        placement.ReviseLifecycleTarget(null);
        Assert.Null(placement.Title);
        Assert.Null(placement.LifecycleTargetPlacementId);
    }

    private static DocumentPlacement CreatePlacement()
        => new(Guid.NewGuid(), Guid.NewGuid(), "0000", "m1.1", DocumentPlacementOperation.New, "Title");
}
