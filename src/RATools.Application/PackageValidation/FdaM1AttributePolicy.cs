using System.Collections.Frozen;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using RATools.Domain.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// Project-derived mappings, bound to their own bytes and to the captured official originals.
// No repository files or current embedded resources may substitute for archived resources.
internal sealed class FdaM1AttributePolicy
{
    internal const string ResourceKey = "fda-m1-attribute-policy";
    internal const string Digest = "b4ed79e15ac4fe37e32496495c34fa209001adbb16bc784f16d1a25f0d513ed9";
    private sealed record DictionaryPolicy(string Attribute, string[] Elements, FrozenSet<string> Active);
    private sealed record AttributePolicy(string Element, string Attribute, string[] Values);
    private sealed record Combination(string[] Applications, string[] Subtypes);
    private readonly List<DictionaryPolicy> _dictionaries = [];
    private readonly List<AttributePolicy> _required = [], _allowed = [];
    private readonly Dictionary<string, Combination> _combinations = new(StringComparer.Ordinal);
    private readonly FrozenSet<string> _primary, _crossReference;

    private FdaM1AttributePolicy(JsonElement policy, IReadOnlyDictionary<string, StandardsSnapshotResource> resources)
    {
        static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
        static string[] Strings(JsonElement value, string name) => value.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();
        static byte[] Source(JsonElement source, IReadOnlyDictionary<string, StandardsSnapshotResource> resources)
        {
            var key = Text(source, "key");
            if (!resources.TryGetValue(key, out var captured) || captured.Sha256 != Text(source, "sha256"))
                throw new InvalidDataException("A pinned M1 source is absent or unsupported.");
            return captured.ReadBytes();
        }
        if (policy.GetProperty("schemaVersion").GetInt32() != 1 || Text(policy, "algorithm") != "fda-m1-attributes-v1" ||
            Text(policy, "classification") != "ProjectDerivedEvidence") throw new InvalidDataException("Unsupported M1 policy.");
        foreach (var source in policy.GetProperty("sources").EnumerateArray()) _ = Source(source, resources);
        foreach (var dictionary in policy.GetProperty("dictionaries").EnumerateArray())
        {
            using var stream = new MemoryStream(Source(dictionary, resources));
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Missing dictionary root.");
            if (root.Name != Text(dictionary, "root") || (string?)root.Attribute("version-number") != Text(dictionary, "version"))
                throw new InvalidDataException("Unsupported dictionary version.");
            _dictionaries.Add(new(Text(dictionary, "attribute"), Strings(dictionary, "elements"),
                root.Elements("code-display").Where(entry => (string?)entry.Attribute("status") == "active")
                    .Select(entry => (string?)entry.Attribute("code") ?? throw new InvalidDataException("Missing dictionary code.")).ToFrozenSet(StringComparer.Ordinal)));
        }
        foreach (var item in policy.GetProperty("requiredAttributes").EnumerateArray())
            _required.Add(new(Text(item, "element"), Text(item, "attribute"), []));
        foreach (var item in policy.GetProperty("allowedAttributes").EnumerateArray())
            _allowed.Add(new(Text(item, "element"), Text(item, "attribute"), Strings(item, "values")));
        _primary = Strings(policy, "primaryApplicationTypes").ToFrozenSet(StringComparer.Ordinal);
        _crossReference = Strings(policy, "crossReferenceApplicationTypes").ToFrozenSet(StringComparer.Ordinal);
        foreach (var item in policy.GetProperty("combinations").EnumerateArray())
            _combinations.Add(Text(item, "submissionType"), new(Strings(item, "applicationTypes"), Strings(item, "submissionSubtypes")));
    }

    internal static FdaM1AttributePolicy? FromSnapshot(StandardsProfileSnapshot? snapshot, ValidationRuleCatalog catalog)
    {
        if (snapshot is null) return null;
        if (!catalog.Sources.TryGetValue("ratools-fda-m1-attributes-v1", out var declared) ||
            declared.Kind != RuleSourceKind.ProjectPolicy || declared.Version != "fda-m1-attributes-v1" ||
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

    internal static string? Attribute(ParsedXmlElement element, string name)
    {
        if (name.StartsWith("xlink:", StringComparison.Ordinal)) return element.Attribute(name[6..], "http://www.w3c.org/1999/xlink");
        if (name.StartsWith("xmlns:", StringComparison.Ordinal)) return element.Attribute(name[6..], "http://www.w3.org/2000/xmlns/");
        return element.Attribute(name, "");
    }
    private static bool Is(ParsedXmlElement element, string name) => name == "fda-regional:fda-regional"
        ? element.LocalName == "fda-regional" && element.NamespaceUri == "http://www.ich.org/fda"
        : element.LocalName == name && element.NamespaceUri.Length == 0;

    internal void Inspect(ParsedBackbone document, string rule,
        Action<ParsedXmlElement, string, string> fail, Action<ParsedXmlElement, string, string> incomplete)
    {
        if (rule == "2012")
        {
            foreach (var required in _required)
                foreach (var element in document.Elements.Where(element => Is(element, required.Element)))
                    if (string.IsNullOrWhiteSpace(Attribute(element, required.Attribute)))
                        fail(element, "@" + required.Attribute, "FDA 2012: a required M1 attribute is missing or blank.");
        }
        else if (rule is "1496" or "2013")
        {
            foreach (var dictionary in _dictionaries.Where(dictionary => rule == "2013" || dictionary.Attribute == "application-type"))
                foreach (var element in document.Elements.Where(element => dictionary.Elements.Any(name => Is(element, name))))
                {
                    var value = Attribute(element, dictionary.Attribute);
                    // Absence belongs to 2012; present values (including blank) must match exact codes.
                    if (value is null) continue;
                    var valid = dictionary.Active.Contains(value);
                    if (dictionary.Attribute == "application-type") valid &= (element.LocalName == "application-number" ? _primary : _crossReference).Contains(value);
                    if (!valid) fail(element, "@" + dictionary.Attribute, "FDA " + rule + ": value is not an active permitted code in the selected official dictionary snapshot.");
                }
            if (rule == "2013")
                foreach (var allowed in _allowed)
                    foreach (var element in document.Elements.Where(element => Is(element, allowed.Element)))
                        if (Attribute(element, allowed.Attribute) is { } value && !allowed.Values.Contains(value, StringComparer.Ordinal))
                            fail(element, "@" + allowed.Attribute, "FDA 2013: value is outside the captured DTD enumeration or fixed value.");
        }
        else InspectCombinations(document, rule, fail, incomplete);
    }

    private void InspectCombinations(ParsedBackbone document, string rule,
        Action<ParsedXmlElement, string, string> fail, Action<ParsedXmlElement, string, string> incomplete)
    {
        var byPath = document.Elements.ToDictionary(element => element.NodePath, StringComparer.Ordinal);
        IEnumerable<ParsedXmlElement> Children(ParsedXmlElement parent, string name) => parent.Children.Select(path => byPath[path]).Where(element => Is(element, name));
        var applications = document.Elements.Take(1).SelectMany(root => Children(root, "admin"))
            .SelectMany(admin => Children(admin, "application-set")).SelectMany(set => Children(set, "application")).ToArray();
        if (applications.Length == 0) incomplete(document.Elements[0], "application", "No identifiable application supplies combination inputs.");
        foreach (var application in applications)
        {
            ParsedXmlElement? Single(string parent, string name)
            {
                var parents = Children(application, parent).ToArray();
                var children = parents.SelectMany(item => Children(item, name)).ToArray();
                return parents.Length == 1 && children.Length == 1 ? children[0] : null;
            }
            var number = Single("application-information", "application-number");
            var submission = Single("submission-information", "submission-id");
            var sequence = Single("submission-information", "sequence-number");
            var app = number is null ? null : Attribute(number, "application-type");
            var type = submission is null ? null : Attribute(submission, "submission-type");
            var subtype = sequence is null ? null : Attribute(sequence, "submission-sub-type");
            var knownApp = app is not null && _primary.Contains(app);
            if (type is null || !_combinations.TryGetValue(type, out var combination))
            {
                incomplete(submission ?? application, "@submission-type", "An unambiguous official submission type is required for combination evaluation.");
                continue;
            }
            if (rule == "2034")
            {
                if (!knownApp) incomplete(number ?? application, "@application-type", "An unambiguous permitted application type is required.");
                else if (!combination.Applications.Contains(app!, StringComparer.Ordinal))
                    fail(submission!, "@submission-type", "FDA 2034: submission type is not permitted for this application type.");
                continue;
            }
            var knownSubtype = subtype is not null && _dictionaries.Single(item => item.Attribute == "submission-sub-type").Active.Contains(subtype);
            // Preserve an observed subtype conflict even when the application field is incomplete.
            if (knownSubtype && !combination.Subtypes.Contains(subtype!, StringComparer.Ordinal))
                fail(sequence!, "@submission-sub-type", "FDA 2022: submission subtype is not permitted for this submission type.");
            else if (!knownSubtype || !knownApp || !combination.Applications.Contains(app!, StringComparer.Ordinal))
                incomplete(sequence ?? application, "@submission-sub-type", "FDA 2022 requires an unambiguous supported application/submission pair and subtype.");
        }
    }
}
