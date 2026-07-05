using RATools.Application.Validation.Dtos;

namespace RATools.Application.Validation;

internal sealed record PublishReadinessFindingSummary(
    int BlockingErrorCount,
    int WarningCount,
    IReadOnlyCollection<PublishReadinessCategorySummaryDto> CategorySummaries)
{
    public static PublishReadinessFindingSummary Create(IEnumerable<PublishReadinessFindingDto> findings)
    {
        var blockingErrorCount = 0;
        var warningCount = 0;
        var categories = new Dictionary<string, CategoryCounts>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in findings)
        {
            var isError = string.Equals(finding.Severity, "Error", StringComparison.OrdinalIgnoreCase);
            var isWarning = string.Equals(finding.Severity, "Warning", StringComparison.OrdinalIgnoreCase);

            if (isError)
            {
                blockingErrorCount++;
            }
            else if (isWarning)
            {
                warningCount++;
            }

            if (!categories.TryGetValue(finding.Category, out var category))
            {
                category = new CategoryCounts(finding.Category);
                categories[finding.Category] = category;
            }

            category.TotalCount++;
            if (isError)
            {
                category.ErrorCount++;
            }
            else if (isWarning)
            {
                category.WarningCount++;
            }
        }

        var categorySummaries = categories.Values
            .Select(x => new PublishReadinessCategorySummaryDto(
                x.Category,
                x.ErrorCount,
                x.WarningCount,
                x.TotalCount))
            .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PublishReadinessFindingSummary(blockingErrorCount, warningCount, categorySummaries);
    }

    private sealed class CategoryCounts(string category)
    {
        public string Category { get; } = category;

        public int ErrorCount { get; set; }

        public int WarningCount { get; set; }

        public int TotalCount { get; set; }
    }
}
