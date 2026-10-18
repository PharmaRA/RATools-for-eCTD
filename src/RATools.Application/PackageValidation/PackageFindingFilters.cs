using System.Text.Json;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public static class PackageFindingFilters
{
    public static string[]? RuleIds(PackageValidationRun? run, string? category)
    {
        if (category is null) return null;
        if (run is null) return [];
        run.VerifyRequestIntegrity();
        // Category membership belongs to this run's archived catalog, not today's rules.
        using var request = JsonDocument.Parse(run.RequestJson);
        return request.RootElement.GetProperty("ruleCatalog").GetProperty("rules").EnumerateArray()
            .Where(rule => rule.GetProperty("category").GetString() == category)
            .Select(rule => rule.GetProperty("internalRuleId").GetString()!).ToArray();
    }
}
