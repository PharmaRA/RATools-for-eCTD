using RATools.Domain.PackageValidation;
using System.Text;

namespace RATools.Application.PackageValidation;

internal static class Fda47RegionalRules
{
    internal static readonly string[] Ids = ["FDA47-2035", "FDA47-2036", "FDA47-2037", "FDA47-2038", "FDA47-2042",
        "FDA47-2044", "FDA47-3036", "FDA47-3050", "FDA47-3065"];

    internal static bool IsOfficial(ValidationRuleDescriptor rule) => Ids.Contains(rule.InternalRuleId, StringComparer.Ordinal);
    internal static DateOnly EffectiveFrom(ValidationRuleDescriptor rule) => rule.AuthorityRuleId is "3036" or "3050" ? new(2008, 3, 10) : new(2015, 6, 15);

    internal static bool HasPinnedSource(ValidationRuleDescriptor rule, ValidationRuleCatalog catalog) =>
        rule.AuthorityRuleId == rule.InternalRuleId[6..] && rule.RuleVersion == "4.7.1" &&
        catalog.Sources.TryGetValue(rule.SourceId, out var source) && source.Id == "fda-validation-criteria-4.7" &&
        source.Version == "4.7" && source.VerificationStatus == "Verified" &&
        source.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";

    internal static void Inspect(ParsedBackbone document, ValidationRuleDescriptor rule,
        Action<ParsedXmlElement, string, string> fail)
    {
        var elements = document.Elements.ToDictionary(element => element.NodePath, StringComparer.Ordinal);
        IEnumerable<ParsedXmlElement> Children(ParsedXmlElement parent, string name) =>
            parent.Children.Select(path => elements[path]).Where(element => element.LocalName == name);
        var roots = document.Elements.Take(1);
        var admins = roots.SelectMany(root => Children(root, "admin")).ToArray();
        var applicants = admins.SelectMany(admin => Children(admin, "applicant-info")).ToArray();
        var applications = admins.SelectMany(admin => Children(admin, "application-set"))
            .SelectMany(set => Children(set, "application")).ToArray();
        var information = applications.SelectMany(application => Children(application, "application-information")).ToArray();
        var submissions = applications.SelectMany(application => Children(application, "submission-information")).ToArray();
        IEnumerable<ParsedXmlElement> values = rule.AuthorityRuleId switch
        {
            "2035" => applicants.SelectMany(applicant => Children(applicant, "submission-description")),
            "2038" => information.SelectMany(info => Children(info, "cross-reference-application-number")),
            "2042" => applicants.SelectMany(applicant => Children(applicant, "applicant-contacts"))
                .SelectMany(contacts => Children(contacts, "applicant-contact"))
                .SelectMany(contact => Children(contact, "emails")).SelectMany(emails => Children(emails, "email")),
            "2044" => applicants.SelectMany(applicant => Children(applicant, "id")),
            "3036" => information.SelectMany(info => Children(info, "application-number")),
            "3050" => submissions.SelectMany(submission => Children(submission, "sequence-number")),
            "3065" => submissions.SelectMany(submission => Children(submission, "submission-id")),
            _ => []
        };
        if (rule.AuthorityRuleId is "2036" or "2037")
        {
            var owners = applications.Where(application => application.Attribute("application-containing-files") == "true").ToArray();
            if (rule.AuthorityRuleId == "2036" && owners.Length == 0 || rule.AuthorityRuleId == "2037" && owners.Length > 1)
                fail(owners.FirstOrDefault() ?? admins.FirstOrDefault() ?? document.Elements[0], "@application-containing-files",
                    "FDA " + rule.AuthorityRuleId + ": expected exactly one application containing files; observed " + owners.Length + ".");
            return;
        }
        foreach (var element in values)
        {
            if (rule.AuthorityRuleId is "2035" or "2042")
            {
                var maximum = rule.AuthorityRuleId == "2035" ? 128 : 64;
                // XML characters are Unicode scalar values, not UTF-16 code units.
                if (element.Text.EnumerateRunes().Count() > maximum)
                    fail(element, element.LocalName, "FDA " + rule.AuthorityRuleId + ": value exceeds " + maximum + " characters.");
            }
            else
            {
                var digits = rule.AuthorityRuleId switch { "2044" => 9, "3036" or "2038" => 6, _ => 4 };
                if (element.Text.Length != digits || element.Text.Any(character => character is < '0' or > '9'))
                    fail(element, element.LocalName, "FDA " + rule.AuthorityRuleId + ": expected exactly " + digits + " ASCII numeric digits.");
            }
        }
    }
}
