using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RATools.Application.Standards;
using RATools.Application.RegionalMetadata;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Standards;

namespace RATools.Application.Applications;

public sealed record RegionalImportParseResult(RegionalMetadataContent Content, IReadOnlyList<RegionalMetadataDiagnostic> Diagnostics);

public static class RegionalImportParser
{
    public static RegionalImportParseResult Parse(byte[] originalBytes, bool isEu, string sequenceNumber, StandardsProfileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(originalBytes); ArgumentNullException.ThrowIfNull(snapshot);
        if (originalBytes.Length is < 1 or > 8 * 1024 * 1024) throw new XmlException("Regional XML exceeds the bounded evidence budget.");
        snapshot.Verify();
        var template = isEu ? "eu-ectd-3.2.2" : "us-fda-ectd-3.2.2";
        if (snapshot.Descriptor.TemplateKey != template) throw new XmlException("Regional import snapshot belongs to another template.");
        using var input = new MemoryStream(originalBytes, false);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        var xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = xml.Root ?? throw new XmlException("Missing regional root.");
        var profile = (isEu ? BackboneXmlProfiles.EuEctd322Regional : BackboneXmlProfiles.FdaEctd322UsRegional33).Regional;
        if (root.Name.LocalName != profile.RootElementName) throw new XmlException("Unexpected regional root.");
        var diagnostics = new List<RegionalMetadataDiagnostic>();
        if (root.Name.NamespaceName != profile.Namespace) diagnostics.Add(new("RegionalUnsupportedNamespace", Path(root), "The original namespace is retained but is not the acquired regional namespace."));
        if (root.Attribute("dtd-version")?.Value != profile.DtdVersion) diagnostics.Add(new("RegionalUnsupportedVersion", Path(root) + "/@dtd-version", "The declared or missing version is not the pinned regional DTD version; imported values remain unqualified."));
        Inspect(root, snapshot, isEu, diagnostics);
        var digest = ImportedRegionalEvidence.Hash(originalBytes);
        Guid Id(XElement element) => StableId(digest + ":" + Path(element));
        IReadOnlyList<RegionalTextEntry> Texts(IEnumerable<XElement> values) => values.Select(value => new RegionalTextEntry(Id(value), Scalar(value))).ToArray();
        RegionalMetadataContent content;
        if (isEu)
        {
            var wrapper = One(root, "eu-envelope");
            var envelopes = new List<EuEnvelope>();
            foreach (var envelope in Children(wrapper, "envelope"))
            {
                var submission = One(envelope, "submission"); var tracking = One(submission, "procedure-tracking");
                var legacyTracking = One(submission, "tracking");
                if (tracking is not null && legacyTracking is not null) throw new XmlException("Ambiguous parallel procedure-tracking and legacy tracking.");
                if (legacyTracking is not null) diagnostics.Add(new("RegionalUnsupportedShape", Path(legacyTracking), "Legacy tracking is preserved as an explicit unqualified compatibility mapping."));
                var sequence = One(envelope, "sequence"); CheckSequence(sequence, sequenceNumber);
                var related = Children(envelope, "related-sequence").Select(value =>
                {
                    var number = Scalar(value) ?? "";
                    if (number.Length != 4 || number.Any(character => character is < '0' or > '9')) throw new XmlException("Invalid related-sequence cannot be represented safely.");
                    return new RegionalRelatedSequence(Id(value), number);
                }).ToArray();
                envelopes.Add(new(Id(envelope), Value(envelope, "identifier"), Attr(envelope, "country"), Attr(One(envelope, "agency"), "code"),
                    Attr(One(envelope, "procedure"), "type"), Attr(submission, "type"), Attr(submission, "mode"), Value(submission, "number"),
                    Attr(One(envelope, "submission-unit"), "type"), Value(envelope, "applicant"), Texts(Children(tracking ?? legacyTracking, "number")),
                    Texts(Children(envelope, "invented-name")), Texts(Children(envelope, "inn")), related, Value(envelope, "submission-description")));
            }
            content = new(1, "EU", Eu: new(envelopes));
        }
        else
        {
            var admin = One(root, "admin"); var applicant = One(admin, "applicant-info");
            var applications = Children(One(admin, "application-set"), "application").ToArray();
            if (applications.Length > 1) throw new XmlException("Multiple FDA applications cannot be collapsed into one imported application.");
            var application = applications.SingleOrDefault(); var information = One(application, "application-information");
            var submission = One(application, "submission-information"); var number = One(information, "application-number");
            var sequence = One(submission, "sequence-number"); CheckSequence(sequence, sequenceNumber);
            var contacts = new List<RegionalContact>();
            foreach (var contact in Children(One(applicant, "applicant-contacts"), "applicant-contact"))
            {
                var name = One(contact, "applicant-contact-name");
                contacts.Add(new(Id(contact), Scalar(name), Attr(name, "applicant-contact-type"),
                    Children(One(contact, "telephones"), "telephone").Select(value => new RegionalTelephone(Id(value), Scalar(value), Attr(value, "telephone-number-type"))).ToArray(),
                    Children(One(contact, "emails"), "email").Select(value => new RegionalEmail(Id(value), Scalar(value))).ToArray()));
            }
            content = new(1, "US", Us: new(Scalar(number), Attr(number, "application-type"), Value(applicant, "company-name"), contacts,
                Attr(One(submission, "submission-id"), "submission-type"), Attr(sequence, "submission-sub-type"), Attr(One(submission, "form"), "form-type"), Value(applicant, "submission-description"),
                Value(applicant, "id"), Value(submission, "submission-id")));
            foreach (var value in root.Descendants().Where(value => value.Name.LocalName == "cross-reference-application-number"))
                diagnostics.Add(new("RegionalUnmappedValue", Path(value), "This value has no typed regional field and remains in the hash-bound original XML."));
        }
        var relatedSequences = content.Eu?.Envelopes.SelectMany(item => item.RelatedSequences).Select(item => item.SequenceNumber) ?? [];
        try
        {
            diagnostics.AddRange(RegionalMetadataValidation.Sequence(content, snapshot, relatedSequences.Append(sequenceNumber).ToHashSet(StringComparer.Ordinal),
                sequenceNumber, [], [], new Dictionary<Guid, string>()));
        }
        catch (RegionalMetadataScopeException)
        { diagnostics.Add(new("RegionalRelatedSequenceUnqualified", "eu.envelopes", "Related sequence business scope is not established by the imported XML; exact values remain an unqualified draft.")); }
        // Domain limits also apply to parser output, before any imported sequence is published.
        _ = RegionalMetadataRevision.Create(Guid.NewGuid(), Guid.NewGuid(), sequenceNumber, 1, 1, snapshot.Id, content, null, [], [], null, null, [], diagnostics, null, null, DateTime.UtcNow);
        return new(content, diagnostics);
    }

    public static RegionalDocumentContext Context(XElement leaf, Guid placementId, Guid? nodeInstanceId)
    {
        var ancestors = leaf.AncestorsAndSelf().ToArray();
        string? Inherited(XName name) => ancestors.Select(value => value.Attribute(name)?.Value).FirstOrDefault(value => value is not null);
        return new(placementId, nodeInstanceId, Inherited("country"), Inherited(XNamespace.Xml + "lang"),
            ancestors.FirstOrDefault(value => value.Name.LocalName == "pi-doc")?.Attribute("type")?.Value,
            ancestors.FirstOrDefault(value => value.Name.LocalName == "form")?.Attribute("form-type")?.Value);
    }

    public static string Path(XElement element) => "/" + string.Join("/", element.AncestorsAndSelf().Reverse().Select(value => value.Name.LocalName + "[" + (value.ElementsBeforeSelf(value.Name).Count() + 1) + "]"));
    private static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
    private static IEnumerable<XElement> Children(XElement? element, string name) => element?.Elements().Where(value => value.Name.LocalName == name && value.Name.NamespaceName.Length == 0) ?? [];
    private static XElement? One(XElement? element, string name)
    { var values = Children(element, name).ToArray(); if (values.Length > 1) throw new XmlException($"Ambiguous repeated scalar/container '{name}' at '{(element is null ? "/" : Path(element))}'."); return values.SingleOrDefault(); }
    private static string? Value(XElement? element, string name) => Scalar(One(element, name));
    private static string? Scalar(XElement? element)
    { if (element?.HasElements == true) throw new XmlException("Nested elements make scalar regional metadata ambiguous: " + Path(element)); return element?.Value; }
    private static string? Attr(XElement? element, string name) => element?.Attribute(name)?.Value;
    private static void CheckSequence(XElement? element, string sequence)
    { if (element is not null && Scalar(element) != sequence) throw new XmlException("Regional sequence number differs from the actual imported sequence."); }
    private static void Inspect(XElement root, StandardsProfileSnapshot snapshot, bool isEu, List<RegionalMetadataDiagnostic> diagnostics)
    {
        var sources = snapshot.Resources.Where(resource => resource.RelativePath.EndsWith(".dtd", StringComparison.Ordinal) || resource.RelativePath.EndsWith(".mod", StringComparison.Ordinal))
            .Where(resource => isEu ? resource.RelativePath.Contains("eu-", StringComparison.Ordinal) : resource.RelativePath.Contains("us-regional", StringComparison.Ordinal));
        var dtd = string.Join("\n", sources.Select(resource => Encoding.UTF8.GetString(resource.ReadBytes())));
        var entities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match declaration in Regex.Matches(dtd, "<!ENTITY\\s+%\\s+([\\w-]+)\\s+([\"'])([\\s\\S]*?)\\2\\s*>", RegexOptions.CultureInvariant)) entities[declaration.Groups[1].Value] = declaration.Groups[3].Value;
        for (var pass = 0; pass < 4; pass++) dtd = Regex.Replace(dtd, @"%([\w-]+);", match => entities.GetValueOrDefault(match.Groups[1].Value, match.Value), RegexOptions.CultureInvariant);
        var known = Regex.Matches(dtd, @"<!ELEMENT\s+([\w:-]+)", RegexOptions.CultureInvariant).Select(match => match.Groups[1].Value.Split(':')[^1]).ToHashSet(StringComparer.Ordinal);
        var children = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Match declaration in Regex.Matches(dtd, @"<!ELEMENT\s+([\w:-]+)\s+([\s\S]*?)>", RegexOptions.CultureInvariant))
        {
            var name = declaration.Groups[1].Value.Split(':')[^1];
            children[name] = Regex.Matches(declaration.Groups[2].Value, @"[A-Za-z][\w:-]*", RegexOptions.CultureInvariant).Select(match => match.Value.Split(':')[^1]).Where(value => value is not ("EMPTY" or "PCDATA" or "ANY")).ToHashSet(StringComparer.Ordinal);
        }
        var attributes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Match declaration in Regex.Matches(dtd, @"<!ATTLIST\s+([\w:-]+)([\s\S]*?)>", RegexOptions.CultureInvariant))
        {
            var name = declaration.Groups[1].Value.Split(':')[^1];
            if (!attributes.TryGetValue(name, out var names)) attributes[name] = names = new(StringComparer.Ordinal);
            foreach (Match attribute in Regex.Matches(declaration.Groups[2].Value, @"(?:^|\n)\s*([\w:-]+)\s+(?:CDATA|ID(?:REFS?)?|NMTOKENS?|\()", RegexOptions.CultureInvariant)) names.Add(attribute.Groups[1].Value);
        }
        foreach (var element in root.DescendantsAndSelf())
        {
            if (diagnostics.Count >= 19000) throw new XmlException("Regional XML diagnostic inventory exceeds its bound.");
            if (!known.Contains(element.Name.LocalName)) diagnostics.Add(new("RegionalUnknownElement", Path(element), "Unknown regional element remains in the original XML evidence."));
            else if (element.Parent is { } parent && children.TryGetValue(parent.Name.LocalName, out var allowed) && !allowed.Contains(element.Name.LocalName))
                diagnostics.Add(new("RegionalUnsupportedShape", Path(element), "A known regional element occurs outside its pinned-DTD parent; original evidence is retained."));
            if (element != root && element.Name.NamespaceName.Length != 0) diagnostics.Add(new("RegionalUnsupportedNamespace", Path(element), "Namespaced regional child is not a supported pinned-DTD shape and remains in original evidence."));
            foreach (var declaration in element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration && attribute.Value != (isEu ? "http://europa.eu.int" : "http://www.ich.org/fda") && attribute.Value != "http://www.w3.org/1999/xlink"))
                diagnostics.Add(new("RegionalUnsupportedNamespace", Path(element) + "/@xmlns" + (declaration.Name.LocalName == "xmlns" ? "" : ":" + declaration.Name.LocalName), "Unknown namespace declaration remains in original regional evidence."));
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                var name = attribute.Name.Namespace == XNamespace.Xml ? "xml:" + attribute.Name.LocalName : attribute.Name.NamespaceName == "http://www.w3.org/1999/xlink" ? "xlink:" + attribute.Name.LocalName : attribute.Name.ToString();
                if (!attributes.TryGetValue(element.Name.LocalName, out var names) || !names.Contains(name))
                    diagnostics.Add(new("RegionalUnknownAttribute", Path(element) + "/@" + name, "Unknown regional attribute remains in the original XML evidence."));
                else if (!isEu && (element.Name.LocalName, name) is
                    ("submission-id", "supplement-effective-date-type") or
                    ("application", "application-containing-files") or
                    ("m1-15-promotional-material", "promotional-material-audience-type") or
                    ("m1-15-2-materials", "promotional-material-doc-type") or
                    ("m1-15-2-1-material", "promotional-material-type" or "material-id" or "issue-date"))
                    diagnostics.Add(new("RegionalUnmappedValue", Path(element) + "/@" + name, "This acquired DTD attribute has no typed regional field and remains in the original XML evidence."));
            }
        }
    }
}
