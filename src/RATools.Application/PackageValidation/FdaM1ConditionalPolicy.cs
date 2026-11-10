using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Application.PackageValidation;

// This policy is authorized only by the selected archived catalog and original source bytes.
internal sealed class FdaM1ConditionalPolicy
{
    internal const string ResourceKey = "fda-m1-conditional-policy";
    internal const string Digest = "a3af8c7479c18843992ecdfe6b02a31c4ab437655f443fdd5099039ac2b40673";
    private readonly Dictionary<string, string[]> _values = new(StringComparer.Ordinal);
    internal IReadOnlyList<string> SupplementSubmissionTypes { get; }
    private readonly string _applicationSubtype;
    private readonly string[] _adminForms;

    private FdaM1ConditionalPolicy(JsonElement policy, IReadOnlyDictionary<string, StandardsSnapshotResource> resources)
    {
        static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
        static string[] Strings(JsonElement value, string name) => value.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();
        static byte[] Source(JsonElement source, IReadOnlyDictionary<string, StandardsSnapshotResource> resources)
        {
            if (!resources.TryGetValue(Text(source, "key"), out var captured) || captured.Sha256 != Text(source, "sha256"))
                throw new InvalidDataException("A pinned conditional M1 source is absent or unsupported.");
            return captured.ReadBytes();
        }
        if (policy.GetProperty("schemaVersion").GetInt32() != 1 || Text(policy, "algorithm") != "fda-m1-conditional-v1" ||
            Text(policy, "classification") != "ProjectDerivedEvidence") throw new InvalidDataException("Unsupported conditional M1 policy.");
        foreach (var source in policy.GetProperty("sources").EnumerateArray()) _ = Source(source, resources);
        foreach (var dictionary in policy.GetProperty("dictionaries").EnumerateArray())
        {
            using var stream = new MemoryStream(Source(dictionary, resources));
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Missing dictionary root.");
            if (root.Name != Text(dictionary, "root") || (string?)root.Attribute("version-number") != Text(dictionary, "version"))
                throw new InvalidDataException("Unsupported dictionary version.");
            _values.Add(Text(dictionary, "root"), root.Elements("code-display").Where(entry => (string?)entry.Attribute("status") == "active")
                .Select(entry => (string?)entry.Attribute("code") ?? throw new InvalidDataException("Missing dictionary code.")).ToArray());
        }
        SupplementSubmissionTypes = Array.AsReadOnly(Strings(policy, "supplementSubmissionTypes"));
        _applicationSubtype = Text(policy, "applicationSubtype");
        _adminForms = Strings(policy, "administrativeFormTypes");
    }

    internal static FdaM1ConditionalPolicy? FromSnapshot(StandardsProfileSnapshot snapshot, ValidationRuleCatalog catalog)
    {
        if (!catalog.Sources.TryGetValue("ratools-fda-m1-conditional-v1", out var declared) ||
            declared.Kind != RuleSourceKind.ProjectPolicy || declared.Version != "fda-m1-conditional-v1" ||
            declared.VerificationStatus != "Verified" || declared.Sha256 != Digest) return null;
        var resources = snapshot.Resources.ToDictionary(resource => resource.Key, StringComparer.Ordinal);
        if (!resources.TryGetValue(ResourceKey, out var policy) || policy.Sha256 != Digest || policy.Category != "RuleMapping") return null;
        try
        {
            using var parsed = JsonDocument.Parse(policy.ReadBytes());
            return new(parsed.RootElement, resources);
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or JsonException) { return null; }
    }

    internal static FdaM1ConditionalPolicy? FromSnapshot(StandardsProfileSnapshot snapshot)
    {
        var rules = snapshot.Resources.SingleOrDefault(resource => resource.Key == "rules");
        if (rules is null) return null;
        using var stream = new MemoryStream(rules.ReadBytes());
        return FromSnapshot(snapshot, PackageValidationCatalog.Load(stream));
    }

    internal IReadOnlyList<string> Values(string attribute) => Array.AsReadOnly(_values[attribute]);
    internal bool? RequiresEffectiveType(string? submissionType, string? subtype)
    {
        if (submissionType is null || !_values["submission-type"].Contains(submissionType, StringComparer.Ordinal)) return null;
        if (!SupplementSubmissionTypes.Contains(submissionType, StringComparer.Ordinal)) return false;
        if (subtype is null || !_values["submission-sub-type"].Contains(subtype, StringComparer.Ordinal)) return null;
        return subtype == _applicationSubtype;
    }

    internal void Inspect(ParsedBackbone document, string rule,
        Action<ParsedXmlElement, string, string> fail, Action<ParsedXmlElement, string, string> incomplete)
    {
        var byPath = document.Elements.ToDictionary(element => element.NodePath, StringComparer.Ordinal);
        IEnumerable<ParsedXmlElement> Children(ParsedXmlElement parent, string name) => parent.Children.Select(path => byPath[path])
            .Where(element => element.LocalName == name && element.NamespaceUri.Length == 0);
        var applications = document.Elements.Take(1).SelectMany(root => Children(root, "admin"))
            .SelectMany(admin => Children(admin, "application-set")).SelectMany(set => Children(set, "application")).ToArray();
        if (applications.Length == 0) incomplete(document.Elements[0], "application", "No identifiable application supplies conditional rule inputs.");
        foreach (var application in applications)
        {
            var parents = Children(application, "submission-information").ToArray();
            if (rule == "2045")
            {
                foreach (var form in parents.SelectMany(parent => Children(parent, "form")))
                {
                    var value = FdaM1AttributePolicy.Attribute(form, "form-type");
                    if (string.IsNullOrWhiteSpace(value)) incomplete(form, "@form-type", "FDA 2045: an explicit form type is needed to establish administrative location eligibility.");
                    else if (!_adminForms.Contains(value, StringComparer.Ordinal))
                        fail(form, "@form-type", "FDA 2045: only Form FDA 1571 and Form FDA 356h may be referenced in administrative submission-information; other forms belong in their specified M1 section.");
                }
                continue;
            }
            var submissions = parents.SelectMany(parent => Children(parent, "submission-id")).ToArray();
            var sequences = parents.SelectMany(parent => Children(parent, "sequence-number")).ToArray();
            if (parents.Length != 1 || submissions.Length != 1 || sequences.Length != 1)
            {
                incomplete(application, "submission-information", "FDA 2041: ambiguous or absent submission metadata cannot establish supplement applicability.");
                continue;
            }
            var submission = submissions[0];
            var required = RequiresEffectiveType(FdaM1AttributePolicy.Attribute(submission, "submission-type"),
                FdaM1AttributePolicy.Attribute(sequences[0], "submission-sub-type"));
            if (required is null) incomplete(submission, "@supplement-effective-date-type", "FDA 2041: supported submission type and subtype are required to establish applicability.");
            else if (required.Value && string.IsNullOrWhiteSpace(FdaM1AttributePolicy.Attribute(submission, "supplement-effective-date-type")))
                fail(submission, "@supplement-effective-date-type", "FDA 2041: a supplement with application subtype requires an explicit supplement effective date type.");
        }
    }
}
