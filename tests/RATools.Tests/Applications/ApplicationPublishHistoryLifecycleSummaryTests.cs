using RATools.Application.Applications;
using RATools.Application.Validation.Dtos;

namespace RATools.Tests.Applications;

public sealed class ApplicationPublishHistoryLifecycleSummaryTests
{
    [Fact]
    public void Create_CountsLifecycleResultCodesInOneSummary()
    {
        var matches = new[]
        {
            Match("MATCHED"),
            Match("REPLACE_TARGET_NOT_FOUND"),
            Match("DELETE_TARGET_NOT_FOUND"),
            Match("APPEND_TARGET_NOT_FOUND"),
            Match("LIFECYCLE_TARGET_AMBIGUOUS"),
            Match("LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE"),
            Match("MATCHED"),
        };

        var summary = ApplicationPublishHistoryLifecycleSummary.Create(matches);

        Assert.Equal(2, summary.MatchedCount);
        Assert.Equal(1, summary.ReplaceTargetNotFoundCount);
        Assert.Equal(1, summary.DeleteTargetNotFoundCount);
        Assert.Equal(1, summary.AppendTargetNotFoundCount);
        Assert.Equal(1, summary.AmbiguousCount);
        Assert.Equal(1, summary.CurrentSequenceCount);
    }

    private static ValidationLifecycleMatchDto Match(string resultCode)
        => new(
            "Replace",
            "0002",
            "m1.1",
            Guid.NewGuid(),
            resultCode,
            "DocumentId",
            ["DocumentId"],
            0,
            [],
            [],
            "Missing");
}
