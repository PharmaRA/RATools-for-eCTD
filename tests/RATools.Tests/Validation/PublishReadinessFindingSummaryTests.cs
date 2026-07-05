using RATools.Application.Validation;
using RATools.Application.Validation.Dtos;

namespace RATools.Tests.Validation;

public sealed class PublishReadinessFindingSummaryTests
{
    [Fact]
    public void Create_CountsOverallAndCategorySeverityInOneSummary()
    {
        var findings = new[]
        {
            Finding("Validation", "Error"),
            Finding("Validation", "Warning"),
            Finding("PackageModel", "Info"),
            Finding("regionalmetadata", "Error"),
        };

        var summary = PublishReadinessFindingSummary.Create(findings);

        Assert.Equal(2, summary.BlockingErrorCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.Equal(
            ["PackageModel", "regionalmetadata", "Validation"],
            summary.CategorySummaries.Select(x => x.Category));
        var validation = Assert.Single(summary.CategorySummaries, x => x.Category == "Validation");
        Assert.Equal(1, validation.BlockingErrorCount);
        Assert.Equal(1, validation.WarningCount);
        Assert.Equal(2, validation.FindingCount);
    }

    private static PublishReadinessFindingDto Finding(string category, string severity)
        => new(
            "Test",
            severity,
            $"{category}_{severity}",
            "message",
            category,
            "action");
}
