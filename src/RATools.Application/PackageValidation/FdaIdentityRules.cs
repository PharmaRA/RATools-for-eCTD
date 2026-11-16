using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaIdentityRules
{
    internal static bool Applies(ValidationRuleDescriptor rule) => Fda47RegionalRules.IsOfficial(rule) && rule.AuthorityRuleId is "1519" or "1714" or "2033";
    internal static void Inspect(ParsedBackbone document, string rule, PackageSourceLayout? layout,
        Action<ParsedXmlElement, string, string> fail, Action<ParsedXmlElement, string, string> incomplete)
    {
        var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
        IEnumerable<ParsedXmlElement> Children(ParsedXmlElement parent, string name) => parent.Children.Select(path => elements[path])
            .Where(item => item.LocalName == name && item.NamespaceUri.Length == 0);
        ParsedXmlElement? Scalar(ParsedXmlElement application, string parent, string name)
        {
            var parents = Children(application, parent).ToArray(); var values = parents.SelectMany(item => Children(item, name)).ToArray();
            return parents.Length == 1 && values.Length == 1 ? values[0] : null;
        }
        static bool Digits(string? value, int length) => value?.Length == length && value.All(character => character is >= '0' and <= '9');
        var applications = document.Elements.Take(1).SelectMany(root => Children(root, "admin")).SelectMany(admin => Children(admin, "application-set"))
            .SelectMany(set => Children(set, "application")).ToArray();
        if (applications.Length == 0) { incomplete(document.Elements[0], "application", "No identifiable application supplies number consistency evidence."); return; }
        if (rule is "1519" or "1714")
        {
            var owners = applications.Where(item => FdaM1AttributePolicy.Attribute(item, "application-containing-files") == "true").ToArray();
            if (owners.Length != 1) { incomplete(document.Elements[0], "@application-containing-files", "Exactly one unambiguous file-owning application is required."); return; }
            var value = rule == "1519" ? Scalar(owners[0], "application-information", "application-number") : Scalar(owners[0], "submission-information", "sequence-number");
            var actual = rule == "1519" ? layout?.ApplicationDirectoryName : layout?.SequenceDirectoryName;
            var length = rule == "1519" ? 6 : 4;
            if (value is null || !Digits(value.Text, length) || !Digits(actual, length))
                incomplete(value ?? owners[0], rule == "1519" ? "application-number" : "sequence-number", "The exact observed numeric directory and unambiguous XML number are required; no directory identity is inferred from request metadata.");
            else if (!string.Equals(value.Text, actual, StringComparison.Ordinal))
                fail(value, value.LocalName, "FDA " + rule + ": file-owning XML number differs from the captured directory name " + actual + ".");
            return;
        }
        var seen = new Dictionary<(string Type, string Number), string>();
        foreach (var application in applications)
        {
            var number = Scalar(application, "application-information", "application-number");
            var sequence = Scalar(application, "submission-information", "sequence-number");
            var type = number is null ? null : FdaM1AttributePolicy.Attribute(number, "application-type");
            if (number is null || sequence is null || !Digits(number.Text, 6) || !Digits(sequence.Text, 4) ||
                type is not ("fdaat1" or "fdaat2" or "fdaat3" or "fdaat4" or "fdaat5" or "fdaat6"))
            { incomplete(application, "application-number", "FDA 2033 requires an unambiguous primary application identity and four-digit sequence."); continue; }
            var identity = (type, number.Text);
            if (seen.TryGetValue(identity, out var previous) && previous != sequence.Text)
                fail(sequence, "sequence-number", "FDA 2033: different regulatory activities for the same application declare different sequence numbers.");
            else seen.TryAdd(identity, sequence.Text);
        }
    }
}
