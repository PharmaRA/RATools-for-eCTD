using System.Text;
using System.Text.RegularExpressions;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Standards;

namespace RATools.Application.RegionalMetadata;

internal static class RegionalMetadataValidation
{
    internal static IReadOnlyList<RegionalMetadataDiagnostic> Defaults(RegionalApplicationDefaultsContent content, StandardsProfileSnapshot snapshot)
    {
        var issues = new List<RegionalMetadataDiagnostic>();
        if (content.Us is { } us) { Required(us.ApplicationIdentifier, "us.applicationIdentifier", issues); Required(us.ApplicationType, "us.applicationType", issues); Required(us.ApplicantName, "us.applicantName", issues); Contacts(us.Contacts, issues); }
        if (content.Eu is { } eu) { Required(eu.Applicant, "eu.applicant", issues); TextList(eu.InventedNames, "eu.inventedNames", true, issues); TextList(eu.ProcedureTrackingNumbers, "eu.procedureTrackingNumbers", true, issues); TextList(eu.Inns, "eu.inns", false, issues); }
        issues.Add(new("RegionalQualificationPending", "standardsSnapshotId", "Captured regional metadata remains a draft until source, mapping, writer, rule and comparison qualifications are complete."));
        snapshot.Verify(); return issues;
    }
    internal static IReadOnlyList<RegionalMetadataDiagnostic> Sequence(RegionalMetadataContent content, StandardsProfileSnapshot snapshot, HashSet<string> sequences,
        string sequence, IReadOnlyList<RegionalNodeContext> nodes, IReadOnlyList<RegionalDocumentContext> documents, IReadOnlyDictionary<Guid, string> sections)
    {
        var issues = new List<RegionalMetadataDiagnostic>();
        if (content.Us is { } us)
        {
            foreach (var (value, path) in new[] { (us.ApplicationIdentifier, "applicationIdentifier"), (us.ApplicationType, "applicationType"), (us.ApplicantName, "applicantName"), (us.ApplicantIdentifier, "applicantIdentifier"), (us.SubmissionIdentifier, "submissionIdentifier"), (us.SubmissionType, "submissionType"), (us.SubmissionSubtype, "submissionSubtype"), (us.Description, "description") }) Required(value, "us." + path, issues);
            Contacts(us.Contacts, issues);
            for (var index = 0; index < nodes.Count; index++) UsContext(nodes[index].Country, nodes[index].Language, nodes[index].ProductInformationType, nodes[index].FormType, sections[nodes[index].NodeInstanceId], $"nodeContexts[{index}]", issues);
            for (var index = 0; index < documents.Count; index++) UsContext(documents[index].Country, documents[index].Language, documents[index].ProductInformationType, documents[index].FormType, sections[documents[index].PlacementId], $"documentContexts[{index}]", issues);
        }
        if (content.Eu is { } eu)
        {
            var envelopeDtd = Resource(snapshot, "xml-eu-envelope"); var regionalDtd = Resource(snapshot, "xml-eu-dtd");
            if (eu.Envelopes.Count == 0) issues.Add(new("Required", "eu.envelopes", "At least one explicit envelope is needed for a complete draft."));
            for (var index = 0; index < eu.Envelopes.Count; index++)
            {
                var value = eu.Envelopes[index]; var path = $"eu.envelopes[{index}]";
                foreach (var (field, name) in new[] { (value.Identifier, "identifier"), (value.Country, "country"), (value.Agency, "agency"), (value.Procedure, "procedure"), (value.SubmissionType, "submissionType"), (value.SubmissionUnit, "submissionUnit"), (value.Applicant, "applicant"), (value.Description, "description") }) Required(field, path + "." + name, issues);
                Allowed(value.Country, Entity(envelopeDtd, "env-countries"), path + ".country", issues);
                Allowed(value.Agency, Attribute(envelopeDtd, "agency", "code"), path + ".agency", issues);
                Allowed(value.Procedure, Attribute(envelopeDtd, "procedure", "type"), path + ".procedure", issues);
                Allowed(value.SubmissionType, Attribute(envelopeDtd, "submission", "type"), path + ".submissionType", issues);
                Allowed(value.SubmissionMode, Attribute(envelopeDtd, "submission", "mode"), path + ".submissionMode", issues);
                Allowed(value.SubmissionUnit, Attribute(envelopeDtd, "submission-unit", "type"), path + ".submissionUnit", issues);
                TextList(value.ProcedureTrackingNumbers, path + ".procedureTrackingNumbers", true, issues); TextList(value.InventedNames, path + ".inventedNames", true, issues); TextList(value.Inns, path + ".inns", false, issues);
                if (value.RelatedSequences.Count == 0) issues.Add(new("Required", path + ".relatedSequences", "Select related sequence references explicitly."));
                for (var relatedIndex = 0; relatedIndex < value.RelatedSequences.Count; relatedIndex++)
                {
                    var reference = value.RelatedSequences[relatedIndex].SequenceNumber; var field = path + $".relatedSequences[{relatedIndex}].sequenceNumber";
                    if (!sequences.Contains(reference) || string.CompareOrdinal(reference, sequence) > 0) throw new RegionalMetadataScopeException("Related sequences must actually exist in this application and cannot be future sequences: " + field);
                    var initial = value.SubmissionUnit is "initial" or "reformat";
                    if (initial ? reference != sequence : string.CompareOrdinal(reference, sequence) >= 0)
                        issues.Add(new("RelatedSequenceNotApplicable", field, "The pinned EU M1 v3.1.1 guide requires current sequence for initial/reformat and an earlier activity sequence for other units."));
                }
            }
            for (var index = 0; index < nodes.Count; index++) Context(nodes[index].Country, nodes[index].Language, nodes[index].ProductInformationType, nodes[index].FormType, sections[nodes[index].NodeInstanceId], $"nodeContexts[{index}]", regionalDtd, issues);
            for (var index = 0; index < documents.Count; index++) Context(documents[index].Country, documents[index].Language, documents[index].ProductInformationType, documents[index].FormType, sections[documents[index].PlacementId], $"documentContexts[{index}]", regionalDtd, issues);
        }
        issues.Add(new("RegionalQualificationPending", "standardsSnapshotId", "Structured metadata alone does not activate a formally qualified regional profile."));
        return issues;
    }
    private static void UsContext(string? country, string? language, string? productInformationType, string? formType, string section, string path, List<RegionalMetadataDiagnostic> issues)
    {
        foreach (var (value, field) in new[] { (country, "country"), (productInformationType, "productInformationType") })
            if (value is not null) issues.Add(new("RegionalFieldNotApplicable", path + "." + field, "This field is outside the scoped US form context."));
        if (string.Equals(section, "m1.1", StringComparison.OrdinalIgnoreCase)) Required(formType, path + ".formType", issues);
        else if (formType is not null) issues.Add(new("RegionalFieldNotApplicable", path + ".formType", "US form context applies only to the exact m1.1 placement or node."));
    }
    private static void Context(string? country, string? language, string? piType, string? formType, string section, string path, string dtd, List<RegionalMetadataDiagnostic> issues)
    {
        Allowed(country, Entity(dtd, "countries"), path + ".country", issues); Allowed(language, Entity(dtd, "languages"), path + ".language", issues);
        Allowed(piType, Attribute(dtd, "pi-doc", "type"), path + ".productInformationType", issues);
        if (section == "m1.3.1") { Required(country, path + ".country", issues); Required(language, path + ".language", issues); Required(piType, path + ".productInformationType", issues); }
        else if (piType is not null) issues.Add(new("ProductInformationContextNotApplicable", path + ".productInformationType", "The acquired EU schema permits PI document types only in the m1.3.1 container."));
        if (formType is not null) issues.Add(new("RegionalFieldNotApplicable", path + ".formType", "FDA form context is not an EU PI attribute."));
    }
    private static void Contacts(IReadOnlyList<RegionalContact> contacts, List<RegionalMetadataDiagnostic> issues)
    {
        if (contacts.Count == 0) issues.Add(new("Required", "us.contacts", "The acquired US DTD requires one or more contacts for complete metadata."));
        for (var index = 0; index < contacts.Count; index++)
        {
            var contact = contacts[index]; var path = $"us.contacts[{index}]"; Required(contact.Name, path + ".name", issues); Required(contact.Type, path + ".type", issues);
            if (contact.Phones.Count == 0) issues.Add(new("Required", path + ".phones", "At least one explicit telephone is needed."));
            if (contact.Emails.Count == 0) issues.Add(new("Required", path + ".emails", "At least one explicit email is needed."));
            for (var phone = 0; phone < contact.Phones.Count; phone++) { Required(contact.Phones[phone].Number, path + $".phones[{phone}].number", issues); Required(contact.Phones[phone].Type, path + $".phones[{phone}].type", issues); }
            for (var email = 0; email < contact.Emails.Count; email++) Required(contact.Emails[email].Value, path + $".emails[{email}].value", issues);
        }
    }
    private static void Required(string? value, string path, List<RegionalMetadataDiagnostic> issues) { if (string.IsNullOrWhiteSpace(value)) issues.Add(new("Required", path, "An explicit value is needed; no default was inferred.")); }
    private static void TextList(IReadOnlyList<RegionalTextEntry> values, string path, bool required, List<RegionalMetadataDiagnostic> issues)
    { if (required && values.Count == 0) issues.Add(new("Required", path, "One or more explicit ordered values are needed.")); for (var index = 0; index < values.Count; index++) Required(values[index].Value, path + $"[{index}].value", issues); }
    private static string Resource(StandardsProfileSnapshot snapshot, string key) => snapshot.Resources.SingleOrDefault(resource => resource.Key == key) is { } resource ? Encoding.UTF8.GetString(resource.ReadBytes()) : "";
    private static string[]? Entity(string dtd, string name) => Values(Regex.Match(dtd, "<!ENTITY\\s+%\\s+" + Regex.Escape(name) + "\\s+\"\\(([^)]*)\\)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
    private static string[]? Attribute(string dtd, string element, string attribute)
    {
        var block = Regex.Match(dtd, "<!ATTLIST\\s+" + Regex.Escape(element) + "\\s+([^>]+)>", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Values(Regex.Match(block.Success ? block.Groups[1].Value : "", "(?:^|\\s)" + Regex.Escape(attribute) + "\\s+\\(([^)]*)\\)", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
    }
    private static string[]? Values(Match match) => match.Success ? match.Groups[1].Value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null;
    private static void Allowed(string? value, string[]? values, string path, List<RegionalMetadataDiagnostic> issues)
    { if (value is null) return; if (values is null) issues.Add(new("VocabularyUnverified", path, "The captured schema does not provide a complete controlled value list for this field.")); else if (!values.Contains(value, StringComparer.Ordinal)) issues.Add(new("ControlledValueNotSupported", path, "The entered value is outside the exact captured DTD value list; it was retained for correction.")); }
}
