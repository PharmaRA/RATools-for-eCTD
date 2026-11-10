using System.Text;
using System.Text.RegularExpressions;
using RATools.Domain.Standards;

namespace RATools.Application.RegionalMetadata;

internal static class RegionalMetadataSchema
{
    internal static RegionalMetadataSchemaDto Create(StandardsProfileSnapshot snapshot, string region)
    {
        snapshot.Verify();
        RegionalSourceReferenceDto Source(string resource, string locator)
        {
            var found = snapshot.Resources.Single(item => item.Key == resource);
            return new(resource, found.Sha256, $"/api/standards-snapshots/{snapshot.Id}/resources/{resource}/content", locator);
        }
        RegionalFieldDescriptor Text(string key, bool required = false, string? evidence = null) => new(key, key, "text", required, [], [],
            Conditions: [], Source: Source(region == "US" ? "xml-us-dtd" : "xml-eu-envelope", "Field mapping pending qualification: " + key),
            BusinessEvidence: evidence ?? "Free text retained explicitly; complete regional business evidence remains unqualified.");
        RegionalFieldDescriptor Object(string key, params RegionalFieldDescriptor[] fields) => new(key, key, "object", true, [], fields, Conditions: []);
        RegionalFieldDescriptor Collection(string key, bool required, params RegionalFieldDescriptor[] fields) => new(key, key, "collection", required, [], fields, "id", []);
        RegionalFieldDescriptor Enum(string key, string resource, string element, string attribute, bool required = false)
        {
            var dtd = Encoding.UTF8.GetString(snapshot.Resources.Single(item => item.Key == resource).ReadBytes());
            var block = Regex.Match(dtd, "<!ATTLIST\\s+" + Regex.Escape(element) + "\\s+([^>]+)>", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var match = Regex.Match(block.Success ? block.Groups[1].Value : "", "(?:^|\\s)" + Regex.Escape(attribute) + "\\s+\\(([^)]*)\\)", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return Values(key, resource, element + "/@" + attribute, match, required);
        }
        RegionalFieldDescriptor Values(string key, string resource, string locator, Match match, bool required)
        {
            var options = match.Success ? match.Groups[1].Value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
            return new(key, key, options.Length > 0 ? "enum" : "text", required, options, [], Conditions: [], Source: Source(resource, locator),
                BusinessEvidence: options.Length > 0 ? "Exact captured DTD values; business applicability remains unqualified." : "Captured DTD supplies free text or no complete business vocabulary.");
        }
        RegionalFieldDescriptor Entity(string key, string resource, string entity)
        {
            var dtd = Encoding.UTF8.GetString(snapshot.Resources.Single(item => item.Key == resource).ReadBytes());
            return Values(key, resource, "%" + entity + ";", Regex.Match(dtd, "<!ENTITY\\s+%\\s+" + Regex.Escape(entity) + "\\s+\"\\(([^)]*)\\)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), false);
        }
        RegionalFieldDescriptor Texts(string key, bool required = false) => Collection(key, required, Text("id", true), Text("value", true));
        RegionalFieldDescriptor[] shared;
        RegionalFieldDescriptor[] sequence;
        RegionalFieldDescriptor[] context;
        if (region == "US")
        {
            const string us = "xml-us-dtd";
            var contacts = Collection("contacts", true, Text("id", true), Text("name", true), Enum("type", us, "applicant-contact-name", "applicant-contact-type", true),
                Collection("phones", true, Text("id", true), Text("number", true), Enum("type", us, "telephone", "telephone-number-type", true)),
                Collection("emails", true, Text("id", true), Text("value", true)));
            shared = [Text("applicationIdentifier", true), Enum("applicationType", us, "application-number", "application-type", true), Text("applicantName", true), contacts];
            sequence = [Object("us", [.. shared, Text("applicantIdentifier", true) with { Label = "申请人／公司编号（9位数字）",
                Source = snapshot.Resources.Any(resource => resource.Key == "official-fda-criteria" && resource.Sha256 == "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c")
                    ? Source("official-fda-criteria", "FDA Validation Criteria 4.7, p. 53, error 2044; admin/applicant-info/id") : Source(us, "admin/applicant-info/id"),
                BusinessEvidence = "填写独立的9位申请人／公司编号，保留前导零；六位申请号不能代替此编号。缺失或格式不正确时可保存草稿，发布前须补正。" },
                Text("submissionIdentifier", true) with { Source = Source(us, "admin/application-set/application/submission-information/submission-id"), BusinessEvidence = "Explicit submission business identifier; no sequence number is inferred." },
                Enum("submissionType", us, "submission-id", "submission-type", true), Enum("submissionSubtype", us, "sequence-number", "submission-sub-type", true),
                Enum("formType", us, "form", "form-type"), Text("description", true)])];
            shared = [Object("us", shared)];
            context = [Text("country"), Text("language"), Text("productInformationType"), Enum("formType", us, "form", "form-type")];
        }
        else
        {
            const string envelope = "xml-eu-envelope"; const string eu = "xml-eu-dtd";
            shared = [Text("applicant", true), Texts("procedureTrackingNumbers", true), Texts("inventedNames", true), Texts("inns")];
            sequence = [Object("eu", Collection("envelopes", true, [Text("id", true), Text("identifier", true), Entity("country", envelope, "env-countries") with { Required = true },
                Enum("agency", envelope, "agency", "code", true), Enum("procedure", envelope, "procedure", "type", true), Enum("submissionType", envelope, "submission", "type", true),
                Enum("submissionMode", envelope, "submission", "mode"), Text("submissionNumber"), Enum("submissionUnit", envelope, "submission-unit", "type", true),
                .. shared, Collection("relatedSequences", true, Text("id", true), Text("sequenceNumber", true)) with { Conditions = [new("eu.envelopes[].submissionUnit", ["initial", "reformat"], "Select current sequence for initial/reformat; an earlier existing activity sequence for other units.")] }, Text("description", true)]))];
            shared = [Object("eu", shared)];
            var applies = new RegionalFieldConditionDto("ctdSection", ["m1.3.1"], "Required in the actual m1.3.1 container; switching context never resets stored values.", "required");
            context = [Entity("country", eu, "countries") with { Conditions = [applies] }, Entity("language", eu, "languages") with { Conditions = [applies] },
                Enum("productInformationType", eu, "pi-doc", "type") with { Conditions = [applies,
                    new("ctdSection", ["m1.3.1"], "PI type applies only in the actual m1.3.1 container; retain existing values outside that container for correction.", "visible")] },
                Text("formType", evidence: "FDA form context is not applicable to EU; retain entered values for correction.")];
        }
        var sources = snapshot.Resources.Where(resource => resource.Category is "DTD" or "ControlledVocabulary" or "Profile")
            .Select(resource => Source(resource.Key, resource.RelativePath)).ToArray();
        return new(1, region, snapshot.Id, sequence, shared, [Text("nodeInstanceId", true), .. context], [Text("placementId", true), Text("nodeInstanceId"), .. context], sources);
    }
}
