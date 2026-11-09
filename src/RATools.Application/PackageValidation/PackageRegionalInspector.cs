using System.Text.RegularExpressions;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// Trial policies over bounded parser facts; complete regulatory qualification is separate.
public sealed class PackageRegionalInspector(PackageStandardsContext? standards = null)
{
    public const string RegionalEngineVersion = "package-regional-inspector-v3";
    private static readonly string[] UsIds = ["US-ADMIN-METADATA", "US-FORM-ATTRIBUTES", "US-SUBMISSION-SCOPE", .. Fda47RegionalRules.Ids];
    private static readonly string[] EuIds = ["EU-ENVELOPE-METADATA", "EU-PI-CONTEXT", "EU-RELATED-SEQUENCE"];

    public PackageRegionalInspection Inspect(PackageInputSet inputs, PackageLifecycleInspection lifecycle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(lifecycle);
        var context = standards ?? PackageStandardsContext.Current;
        if (lifecycle.InputDigest != inputs.Target.InputDigest || lifecycle.HistoryManifestDigest != inputs.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.LimitsDigest != inputs.Target.Limits.Digest())
            throw new ArgumentException("Regional observations require the exact selected input, history, rules and limits.", nameof(lifecycle));
        var selected = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).Where(rule => UsIds.Contains(rule.InternalRuleId, StringComparer.Ordinal) ||
            EuIds.Contains(rule.InternalRuleId, StringComparer.Ordinal)).ToDictionary(rule => rule.InternalRuleId, StringComparer.Ordinal);
        var findings = selected.Keys.ToDictionary(id => id, _ => new List<ValidationFinding>(), StringComparer.Ordinal);
        var observed = selected.Keys.ToDictionary(id => id, _ => false, StringComparer.Ordinal);
        var applicable = selected.Keys.ToDictionary(id => id, _ => false, StringComparer.Ordinal);
        var count = 0;
        var limited = false;
        foreach (var (sequence, capture) in inputs.Sequences.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sequenceContext = context.ForSequence(sequence);
            var profile = inputs.Baseline.Entries.SingleOrDefault(entry => entry.SequenceNumber == sequence)?.ProfileSnapshotId ?? lifecycle.ProfileSnapshotId;
            var regionalProfile = sequenceContext.Assets.ForProfile(profile).SingleOrDefault(item => item.Kind != BackboneKind.Ich);
            if (regionalProfile is null) continue;
            var ids = regionalProfile.Kind == BackboneKind.UsRegional ? UsIds : EuIds;
            // Each selected immutable catalog owns its checks, including legacy history.
            var scoped = sequenceContext.Catalog.ForProfile(profile).Where(rule => ids.Contains(rule.InternalRuleId, StringComparer.Ordinal) && selected.ContainsKey(rule.InternalRuleId));
            var xml = lifecycle.XmlInspections.SingleOrDefault(item => item.InputDigest == capture.InputDigest && item.ProfileSnapshotId == profile);
            var documents = xml?.Backbones.Where(item => item.Kind == regionalProfile.Kind).ToArray() ?? [];
            foreach (var rule in scoped)
            {
                observed[rule.InternalRuleId] = true;
                if (Fda47RegionalRules.IsOfficial(rule))
                {
                    if (!Fda47RegionalRules.HasPinnedSource(rule, sequenceContext.Catalog) || sequenceContext.SelectionDate is null)
                    {
                        Add(rule, CheckStatus.NotEvaluated, "FDA_RULE_POLICY_UNBOUND", "Official checks require the supported pinned rule source and an explicit bound selection date.", new(sequence, sequence + "/" + regionalProfile.DefaultRelativePath));
                        continue;
                    }
                    if (sequenceContext.SelectionDate < Fda47RegionalRules.EffectiveFrom(rule)) continue;
                }
                applicable[rule.InternalRuleId] = true;
                if (documents.Length == 0)
                {
                    Add(rule, xml is null || xml.Backbones.Any(item => !item.ReadComplete) ? CheckStatus.NotEvaluated : CheckStatus.Fail,
                        "REGIONAL_BACKBONE_MISSING", "The declared regional scope has no inspected regional backbone.", new(sequence, sequence + "/" + regionalProfile.DefaultRelativePath));
                    continue;
                }
                foreach (var document in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!document.ReadComplete)
                    {
                        Add(rule, CheckStatus.NotEvaluated, "REGIONAL_PARSE_INCOMPLETE", "Incomplete bounded XML parsing cannot establish regional business coverage.", new(sequence, document.LogicalPath));
                        continue;
                    }
                    var run = new DocumentInspection(inputs, lifecycle, document, rule, sequenceContext.Assets, Add);
                    run.Inspect();
                    if (document.DtdStatus != CheckStatus.Pass)
                        Add(rule, CheckStatus.NotEvaluated, "REGIONAL_DTD_PREREQUISITE", "Observed business defects are retained; DTD-invalid input cannot establish complete coverage.", new(sequence, document.LogicalPath));
                }
            }
        }
        return new(selected.Values.Select(rule =>
        {
            var items = findings[rule.InternalRuleId];
            var status = items.Any(item => item.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail : limited || !observed[rule.InternalRuleId] || items.Any(item => item.CheckStatus == CheckStatus.NotEvaluated) ? CheckStatus.NotEvaluated : !applicable[rule.InternalRuleId] ? CheckStatus.NotApplicable : CheckStatus.Pass;
            return new RuleCheckResult(rule.InternalRuleId, status, $"{(rule.AuthorityRuleId is null ? "ProjectPolicy" : "FDA " + rule.AuthorityRuleId)} {rule.SourceId}: {rule.SourceSection}. " +
                (status == CheckStatus.NotApplicable ? "The bound selection date precedes this rule's effective date. " : "") +
                (status == CheckStatus.Pass ? "Selected regional business checks completed against parsed selected inputs." : "See located defects and unresolved regional scope."), items);
        }));

        void Add(ValidationRuleDescriptor rule, CheckStatus status, string code, string message, ValidationLocation location, string? actual = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= inputs.Target.Limits.MaxXmlFindings - 1) { limited = true; return; }
            count++;
            if (location.SequenceNumber is { } number && inputs.Sequences.TryGetValue(number, out var source) && location.LogicalPath is { } path)
                location = location with { Files = [new(source.SourceId, path)] };
            findings[rule.InternalRuleId].Add(new(rule.InternalRuleId, status, rule.Severity, code, message, location, Actual: actual,
                RecommendedAction: $"Review {rule.SourceId}, {rule.SourceSection}; full regional criteria qualification remains pending."));
        }
    }

    private sealed class DocumentInspection(PackageInputSet inputs, PackageLifecycleInspection lifecycle, ParsedBackbone document,
        ValidationRuleDescriptor rule, PackageXmlAssets assets, Action<ValidationRuleDescriptor, CheckStatus, string, string, ValidationLocation, string?> add)
    {
        private readonly Dictionary<string, ParsedXmlElement> _elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
        private ValidationLocation Location => document.Elements[0].Location;
        private void Issue(CheckStatus status, string code, string message, ParsedXmlElement? element = null, string? actual = null, string? field = null) =>
            add(rule, status, code, message, (element?.Location ?? Location) with { FieldPath = field }, actual);
        private ParsedXmlElement? Child(ParsedXmlElement? parent, string name) => Children(parent, name).FirstOrDefault();
        private IEnumerable<ParsedXmlElement> Children(ParsedXmlElement? parent, string name) => parent?.Children.Select(path => _elements[path]).Where(item => item.LocalName == name) ?? [];
        private void Required(ParsedXmlElement? element, string field, string? attribute = null)
        {
            var value = attribute is null ? element?.Text : element?.Attribute(attribute);
            if (string.IsNullOrWhiteSpace(value)) Issue(CheckStatus.Fail, "REGIONAL_REQUIRED_VALUE", "An explicit nonblank value is required by the selected trial policy.", element, value, field);
        }
        public void Inspect()
        {
            if (document.Kind == BackboneKind.UsRegional) InspectUs(); else InspectEu();
        }
        private void InspectUs()
        {
            if (Fda47RegionalRules.IsOfficial(rule))
                Fda47RegionalRules.Inspect(document, rule, (element, field, message) =>
                    Issue(CheckStatus.Fail, rule.InternalRuleId, message, element, element.Text, field));
            else if (rule.InternalRuleId == "US-ADMIN-METADATA")
            {
                var admin = Child(document.Elements[0], "admin"); var applicant = Child(admin, "applicant-info");
                foreach (var name in new[] { "id", "company-name", "submission-description" }) Required(Child(applicant, name), name);
                var contacts = Children(Child(applicant, "applicant-contacts"), "applicant-contact").ToArray();
                if (contacts.Length == 0) Issue(CheckStatus.Fail, "US_CONTACT_REQUIRED", "At least one explicit ordered applicant contact is required.", applicant);
                foreach (var contact in contacts)
                {
                    var name = Child(contact, "applicant-contact-name");
                    Required(name, "applicant-contact-name"); Required(name, "@applicant-contact-type", "applicant-contact-type");
                    var phones = Children(Child(contact, "telephones"), "telephone").ToArray(); var emails = Children(Child(contact, "emails"), "email").ToArray();
                    if (phones.Length == 0 || emails.Length == 0) Issue(CheckStatus.Fail, "US_CONTACT_CHANNEL_REQUIRED", "Each ordered contact requires explicit telephone and email entries.", contact);
                    foreach (var phone in phones) { Required(phone, "telephone"); Required(phone, "@telephone-number-type", "telephone-number-type"); }
                    foreach (var email in emails) Required(email, "email");
                }
                var applications = Children(Child(admin, "application-set"), "application").ToArray();
                if (applications.Length == 0) Issue(CheckStatus.Fail, "US_APPLICATION_REQUIRED", "The selected US input requires explicit application metadata.", admin);
                foreach (var application in applications)
                {
                    var number = Child(Child(application, "application-information"), "application-number");
                    Required(number, "application-number"); Required(number, "@application-type", "application-type");
                    var submission = Child(application, "submission-information"); var id = Child(submission, "submission-id"); var sequence = Child(submission, "sequence-number");
                    Required(id, "submission-id"); Required(id, "@submission-type", "submission-type");
                    Required(sequence, "sequence-number"); Required(sequence, "@submission-sub-type", "submission-sub-type");
                    if (sequence is not null && sequence.Text != Location.SequenceNumber) Issue(CheckStatus.Fail, "US_SEQUENCE_MISMATCH", "Administrative sequence must identify the actual selected sequence.", sequence, sequence.Text);
                }
            }
            else if (rule.InternalRuleId == "US-FORM-ATTRIBUTES")
            {
                foreach (var element in document.Elements)
                {
                    if (element.LocalName == "form")
                    {
                        Required(element, "@form-type", "form-type");
                        if (element.ParentPath is null || _elements[element.ParentPath].LocalName is not ("submission-information" or "m1-1-forms"))
                            Issue(CheckStatus.Fail, "US_FORM_SCOPE_INVALID", "Forms are supported only in captured DTD admin or M1 forms scope.", element);
                    }
                    else if (element.Attribute("form-type") is { } value) Issue(CheckStatus.Fail, "US_FORM_ATTRIBUTE_OUT_OF_SCOPE", "form-type applies only to a form element.", element, value, "@form-type");
                }
            }
            else if (document.Elements.FirstOrDefault(item => item.LocalName.StartsWith("m1-15", StringComparison.Ordinal)) is { } promotional)
                Issue(CheckStatus.NotEvaluated, "US_PROMOTIONAL_SCOPE_PENDING", "Promotional criteria remain outside the selected administrative/form trial matrix.", promotional);
        }

        private void InspectEu()
        {
            using var envelopeStream = assets.Open("eu-envelope"); using var regionalStream = assets.Open("eu-dtd");
            using var envelopeReader = new StreamReader(envelopeStream); using var regionalReader = new StreamReader(regionalStream);
            var envelopeDtd = envelopeReader.ReadToEnd(); var regionalDtd = regionalReader.ReadToEnd();
            var envelopes = document.Elements.Where(item => item.LocalName == "envelope").ToArray();
            if (envelopes.Length == 0) Issue(CheckStatus.Fail, "EU_ENVELOPE_REQUIRED", "The selected EU scope requires an explicit envelope.");
            if (rule.InternalRuleId == "EU-ENVELOPE-METADATA") InspectEnvelopes(envelopes, envelopeDtd);
            else if (rule.InternalRuleId == "EU-PI-CONTEXT") InspectContexts(regionalDtd);
            else InspectRelated(envelopes, envelopeDtd);
        }
        private void Allowed(ParsedXmlElement? element, string attribute, IReadOnlyList<string> values)
        {
            Required(element, "@" + attribute, attribute);
            var value = element?.Attribute(attribute);
            if (values.Count == 0) Issue(CheckStatus.NotEvaluated, "EU_CAPTURED_VOCABULARY_UNKNOWN", "The exact captured DTD does not supply this vocabulary.", element);
            else if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.Ordinal))
                Issue(CheckStatus.Fail, "EU_CONTROLLED_VALUE_INVALID", "The value is outside the exact captured DTD vocabulary.", element, value, "@" + attribute);
        }
        private void InspectEnvelopes(ParsedXmlElement[] envelopes, string dtd)
        {
            foreach (var envelope in envelopes)
            {
                var country = envelope.Attribute("country"); Allowed(envelope, "country", Entity(dtd, "env-countries"));
                foreach (var name in new[] { "identifier", "applicant", "submission-description", "sequence" }) Required(Child(envelope, name), name);
                foreach (var name in new[] { "invented-name", "inn" })
                {
                    var values = Children(envelope, name).ToArray();
                    if (name == "invented-name" && values.Length == 0) Required(null, name);
                    foreach (var value in values) Required(value, name);
                }
                var submission = Child(envelope, "submission"); var procedure = Child(envelope, "procedure"); var agency = Child(envelope, "agency");
                Allowed(submission, "type", Attribute(dtd, "submission", "type"));
                if (submission?.Attribute("mode") is not null) Allowed(submission, "mode", Attribute(dtd, "submission", "mode"));
                Allowed(Child(envelope, "submission-unit"), "type", Attribute(dtd, "submission-unit", "type"));
                Allowed(procedure, "type", Attribute(dtd, "procedure", "type")); Allowed(agency, "code", Attribute(dtd, "agency", "code"));
                var numbers = Children(Child(submission, "procedure-tracking"), "number").ToArray();
                if (numbers.Length == 0) Required(null, "procedure-tracking/number");
                foreach (var number in numbers.Concat(Children(submission, "number"))) Required(number, "number");
                var own = Child(envelope, "sequence");
                if (own is not null && own.Text != Location.SequenceNumber) Issue(CheckStatus.Fail, "EU_SEQUENCE_MISMATCH", "Envelope sequence must identify the actual selected sequence.", own, own.Text);
                var agencyMatches = country switch { "de" => agency?.Attribute("code") is "DE-BFARM" or "DE-PEI", "fr" => agency?.Attribute("code") == "FR-ANSM", "be" => agency?.Attribute("code") == "BE-FAMHP", _ => true };
                if (procedure?.Attribute("type") == "national" && !agencyMatches) Issue(CheckStatus.Fail, "EU_NATIONAL_AGENCY_MISMATCH", "The selected national DE/FR/BE policy requires the declared country's agency.", agency, agency?.Attribute("code"));
                if (country is not ("de" or "fr" or "be") || procedure?.Attribute("type") != "national")
                    Issue(CheckStatus.NotEvaluated, "EU_BUSINESS_SCOPE_PENDING", "Other countries and procedures remain outside the selected national DE/FR/BE trial matrix.", envelope);
            }
            if (document.Elements.Any(item => item.LocalName == "m1-6-1-non-gmo") && document.Elements.Any(item => item.LocalName == "m1-6-2-gmo"))
                Issue(CheckStatus.Fail, "EU_ENVIRONMENTAL_COMBINATION", "The captured DTD permits one environmental branch, not both.");
        }
        private void InspectContexts(string dtd)
        {
            foreach (var element in document.Elements)
            {
                if (element.Attribute("form-type") is not null) Issue(CheckStatus.Fail, "EU_US_FORM_ATTRIBUTE", "FDA form context is outside selected EU business scope.", element, field: "@form-type");
                if (element.LocalName is "specific" or "pi-doc")
                {
                    Allowed(element, "country", Entity(dtd, "countries"));
                    if (element.Attribute("country") is not ("de" or "fr" or "be")) Issue(CheckStatus.NotEvaluated, "EU_COUNTRY_CONTEXT_PENDING", "This country context is outside the selected national DE/FR/BE trial matrix.", element);
                }
                if (element.LocalName != "pi-doc") continue;
                Allowed(element, "xml:lang", Entity(dtd, "languages")); Allowed(element, "type", Attribute(dtd, "pi-doc", "type"));
                if (element.ParentPath is null || _elements[element.ParentPath].LocalName != "m1-3-1-spc-label-pl")
                    Issue(CheckStatus.Fail, "EU_PI_SCOPE_INVALID", "PI wrappers require the exact captured product-information section.", element);
            }
        }
        private void InspectRelated(ParsedXmlElement[] envelopes, string dtd)
        {
            foreach (var envelope in envelopes)
            {
                var unit = Child(envelope, "submission-unit")?.Attribute("type"); var related = Children(envelope, "related-sequence").ToArray();
                if (related.Length == 0) Issue(CheckStatus.Fail, "EU_RELATED_SEQUENCE_REQUIRED", "Related sequence entries must be explicitly supplied.", envelope);
                foreach (var reference in related)
                {
                    var number = reference.Text;
                    if (number.Length != 4 || number.Any(character => character is < '0' or > '9') || string.CompareOrdinal(number, Location.SequenceNumber) > 0)
                    { Issue(CheckStatus.Fail, "EU_RELATED_SEQUENCE_INVALID", "A related sequence must be four ASCII digits in this application's nonfuture timeline.", reference, number); continue; }
                    if (string.IsNullOrWhiteSpace(unit) || !Attribute(dtd, "submission-unit", "type").Contains(unit, StringComparer.Ordinal))
                    { Issue(CheckStatus.NotEvaluated, "EU_RELATED_UNIT_UNKNOWN", "A valid explicit submission unit is required to interpret related sequences.", reference); continue; }
                    if (unit is "initial" or "reformat" ? number != Location.SequenceNumber : string.CompareOrdinal(number, Location.SequenceNumber) >= 0)
                    { Issue(CheckStatus.Fail, "EU_RELATED_SEQUENCE_NOT_APPLICABLE", "Initial/reformat refer to current sequence; other units refer to an earlier activity sequence.", reference, number); continue; }
                    if (!inputs.Sequences.TryGetValue(number, out var selected))
                    { Issue(CheckStatus.NotEvaluated, "EU_RELATED_HISTORY_NOT_SELECTED", "The exact earlier sequence is absent from the selected same-application input set.", reference, number); continue; }
                    var historicalDocuments = lifecycle.XmlInspections.Where(xml => xml.InputDigest == selected.InputDigest)
                        .SelectMany(xml => xml.Backbones).Where(backbone => backbone.Kind == BackboneKind.EuRegional).ToArray();
                    if (!selected.Manifest.Files.Any(file => file.LogicalPath == number + "/index.xml") || historicalDocuments.Length == 0 ||
                        historicalDocuments.Any(backbone => !backbone.ReadComplete || backbone.DtdStatus != CheckStatus.Pass))
                    { Issue(CheckStatus.NotEvaluated, "EU_RELATED_HISTORY_UNINSPECTED", "The selected sequence needs its actual complete EU backbone to establish a related activity.", reference, number); continue; }
                    var businessIdentity = Child(envelope, "identifier")?.Text;
                    var historicalEnvelopes = historicalDocuments.SelectMany(backbone => backbone.Elements.Where(element => element.LocalName == "envelope")
                        .Select(item => new
                        {
                            Identifiers = backbone.Elements.Where(element => element.ParentPath == item.NodePath && element.LocalName == "identifier").ToArray(),
                            Sequences = backbone.Elements.Where(element => element.ParentPath == item.NodePath && element.LocalName == "sequence").ToArray()
                        })).ToArray();
                    if (string.IsNullOrWhiteSpace(businessIdentity) || historicalEnvelopes.Length == 0 ||
                        historicalEnvelopes.Any(item => item.Identifiers.Length != 1 || string.IsNullOrWhiteSpace(item.Identifiers[0].Text)))
                    { Issue(CheckStatus.NotEvaluated, "EU_RELATED_BUSINESS_IDENTITY_UNKNOWN", "Actual unambiguous parsed envelope identifiers are required to establish same-application business identity.", reference); continue; }
                    var matchingEnvelopes = historicalEnvelopes.Where(item => item.Identifiers[0].Text == businessIdentity).ToArray();
                    if (matchingEnvelopes.Length == 0)
                    { Issue(CheckStatus.Fail, "EU_RELATED_FOREIGN_BUSINESS_IDENTITY", "The selected historical source's actual envelope identifiers do not match this application envelope.", reference, businessIdentity); continue; }
                    foreach (var matched in matchingEnvelopes)
                    {
                        if (matched.Sequences.Length != 1 || string.IsNullOrWhiteSpace(matched.Sequences[0].Text))
                            Issue(CheckStatus.NotEvaluated, "EU_RELATED_HISTORY_SEQUENCE_UNKNOWN", "The matching historical envelope needs its own unambiguous actual sequence declaration.", reference);
                        else if (matched.Sequences[0].Text != number)
                            Issue(CheckStatus.Fail, "EU_RELATED_HISTORY_SEQUENCE_MISMATCH", "The matching historical envelope's sequence does not identify the actual selected referenced source.", reference, matched.Sequences[0].Text);
                    }
                }
            }
        }
        private static string[] Entity(string dtd, string name) => Values(Regex.Match(dtd, "<!ENTITY\\s+%\\s+" + Regex.Escape(name) + "\\s+\"\\(([^\"]+)\\)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups[1].Value);
        private static string[] Attribute(string dtd, string element, string attribute) => Values(Regex.Match(dtd, "<!ATTLIST\\s+" + Regex.Escape(element) + "\\s+(?:(?!>).)*?" + Regex.Escape(attribute) + "\\s*\\(([^)]+)\\)", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups[1].Value);
        private static string[] Values(string value) => value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}

public sealed class PackageRegionalInspection(IEnumerable<RuleCheckResult> checks)
{
    public string EngineVersion { get; } = PackageRegionalInspector.RegionalEngineVersion;
    public IReadOnlyList<RuleCheckResult> Checks { get; } = Array.AsReadOnly(checks.ToDictionary(check => check.RuleId, StringComparer.Ordinal).Values.ToArray());
}
