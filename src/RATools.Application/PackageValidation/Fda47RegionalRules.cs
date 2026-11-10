using RATools.Domain.PackageValidation;
using System.Text;

namespace RATools.Application.PackageValidation;

internal static class Fda47RegionalRules
{
    internal static readonly string[] Ids = ["FDA47-2035", "FDA47-2036", "FDA47-2037", "FDA47-2038", "FDA47-2042",
        "FDA47-2044", "FDA47-3036", "FDA47-3050", "FDA47-3065", "FDA47-1454", "FDA47-2023", "FDA47-2031", "FDA47-2032"];

    internal static bool IsOfficial(ValidationRuleDescriptor rule) => Ids.Contains(rule.InternalRuleId, StringComparer.Ordinal);
    internal static DateOnly EffectiveFrom(ValidationRuleDescriptor rule) => rule.AuthorityRuleId switch
    { "3036" or "3050" => new(2008, 3, 10), "1454" => new(2013, 4, 30), _ => new(2015, 6, 15) };

    internal static bool HasPinnedSource(ValidationRuleDescriptor rule, ValidationRuleCatalog catalog) =>
        rule.AuthorityRuleId == rule.InternalRuleId[6..] && rule.RuleVersion == "4.7.1" &&
        catalog.Sources.TryGetValue(rule.SourceId, out var source) && source.Id == "fda-validation-criteria-4.7" &&
        source.Version == "4.7" && source.VerificationStatus == "Verified" &&
        source.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c";

    internal static void Inspect(ParsedBackbone document, ValidationRuleDescriptor rule,
        Action<ParsedXmlElement, string, string> fail, Action<ParsedXmlElement, string, string> incomplete)
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
        if (rule.AuthorityRuleId == "1454")
        {
            if (applicants.Length == 0) fail(document.Elements[0], "company-name", "FDA 1454: company name is missing.");
            foreach (var applicant in applicants)
            {
                var names = Children(applicant, "company-name").ToArray();
                if (names.Length == 0) fail(applicant, "company-name", "FDA 1454: company name is missing.");
                foreach (var name in names.Where(name => string.IsNullOrWhiteSpace(name.Text)))
                    fail(name, "company-name", "FDA 1454: company name is missing.");
            }
            return;
        }
        if (rule.AuthorityRuleId is "2023" or "2031" or "2032")
        {
            var attribute = rule.AuthorityRuleId switch { "2023" => "submission-sub-type", "2031" => "application-type", _ => "submission-type" };
            var parentName = rule.AuthorityRuleId == "2031" ? "application-information" : "submission-information";
            var elementName = rule.AuthorityRuleId switch { "2023" => "sequence-number", "2031" => "application-number", _ => "submission-id" };
            string? first = null;
            foreach (var application in applications)
            {
                var groupedValues = Children(application, parentName).SelectMany(parent => Children(parent, elementName)).ToArray();
                if (groupedValues.Length != 1)
                {
                    incomplete(application, "@" + attribute, "FDA " + rule.AuthorityRuleId + ": incomplete or ambiguous application metadata cannot establish grouped consistency.");
                    continue;
                }
                foreach (var element in groupedValues)
                {
                    var value = element.Attribute(attribute);
                    if (string.IsNullOrWhiteSpace(value))
                        incomplete(element, "@" + attribute, "FDA " + rule.AuthorityRuleId + ": a missing grouped value cannot establish consistency.");
                    else if (first is null) first = value;
                    else if (!string.Equals(first, value, StringComparison.Ordinal))
                        fail(element, "@" + attribute, "FDA " + rule.AuthorityRuleId + ": grouped applications declare different " + attribute + " values.");
                }
            }
            return;
        }
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
