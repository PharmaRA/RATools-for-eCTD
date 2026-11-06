using RATools.Application.Standards;
using RATools.Application.Validation.Rules.Pdf;
using RATools.Domain.PackageValidation;

namespace RATools.Application.Validation.Rules;

/// <summary>
/// Routes shared eCTD validation rules across the standards profiles currently
/// supported by the multi-region architecture.
/// </summary>
public sealed class RegionalEctdRuleSetProvider : IEctdValidationRuleSetProvider
{
    private readonly IReadOnlyList<EctdValidationRuleSet> _ruleSets;

    public RegionalEctdRuleSetProvider(IEnumerable<IEctdValidationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var available = rules.ToArray();
        _ruleSets =
        [
            Build("us-fda-ectd-3.2.2", "4.5", false),
            Build("eu-ectd-3.2.2", "8.2", false),
            // Compatibility alias for persisted/test profiles created by the
            // pre-v8.2 controlled EU skeleton.
            Build("eu-ectd-3.2.2", "EU", true)
        ];

        EctdValidationRuleSet Build(string profile, string criteria, bool legacy)
        {
            var selected = available.Where(rule => profile == "us-fda-ectd-3.2.2" || rule.RuleId != "FDA-NAMING-1").ToArray();
            var mappings = selected.Select(rule => Mapping(rule, profile, criteria)).ToArray();
            var configured = selected.Select((rule, index) => rule switch
            {
                PdfComplianceRule pdf => (IEctdValidationRule)pdf.WithMapping(mappings[index]),
                FileNamingConventionRule names => names.WithMapping(mappings[index]),
                _ => rule
            }).ToArray();
            return new(profile, criteria, Array.AsReadOnly(configured)) { Mappings = Array.AsReadOnly(mappings), LegacyProfileAlias = legacy };
        }
    }

    private static EctdValidationRuleMapping Mapping(IEctdValidationRule rule, string profile, string criteria)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var severities = new Dictionary<string, EctdValidationSeverity>(StringComparer.Ordinal);
        var locator = "Explicit caller registration; no regulatory qualification inferred";
        if (rule is PdfComplianceRule)
        {
            // PDF 1.4 is the common ICH acceptance. Other versions need regional
            // evidence; existing project recommendations retain their old levels.
            parameters.Add("AcceptedPdfVersions", "1.4");
            parameters.Add("BookmarkRequiredPageCount", "5");
            parameters.Add("MaxRecommendedBookmarkDepth", "4");
            severities.Add("PDF_VERSION_UNQUALIFIED", EctdValidationSeverity.High);
            severities.Add("PDF_BOOKMARK_TOO_DEEP", EctdValidationSeverity.Low);
            locator = "reference/publisher/package-rules-v1.json#PDF-VERSION; ICH eCTD 3.2.2 Appendix 7; PdfComplianceRule project navigation recommendations";
        }
        else if (rule is FileNamingConventionRule)
        {
            parameters.Add("MaxFileNameLength", "64");
            parameters.Add("MaxRelativePathLength", "230");
            locator = "FileNamingConventionRule project filename policy; FDA applicability remains trial-only and source qualification pending";
        }
        return new(rule.RuleId, profile, criteria, RuleSourceKind.ProjectPolicy, "ratools-publisher-contract-v1", locator,
            ["m1", "m2", "m3", "m4", "m5"], new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(parameters), rule.DefaultSeverity,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, EctdValidationSeverity>(severities));
    }

    public EctdValidationRuleSet GetRuleSet(StandardsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var ruleSet in _ruleSets)
        {
            if (string.Equals(profile.TemplateKey, ruleSet.ProfileKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(profile.ValidationCriteriaVersion, ruleSet.ValidationCriteriaVersion, StringComparison.OrdinalIgnoreCase))
            {
                return ruleSet;
            }
        }

        throw new EctdValidationRuleSetNotFoundException(profile.TemplateKey, profile.ValidationCriteriaVersion);
    }
}
