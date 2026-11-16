using System.Security.Cryptography;
using System.Text.Json;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Application.PackageValidation;

internal sealed class FdaSupplementCombinations
{
    internal const string RuleId = "US-SUPPLEMENT-EFFECTIVE-COMBINATION";
    internal const string ResourceKey = "fda-supplement-combinations";
    internal const string Digest = "00584abd2b605d54e2dc6fc71595f989b2736cf965598eb421cc394dbf25e063";
    private readonly Dictionary<string, string[]> _rows;
    private static readonly Lazy<FdaSupplementCombinations> Embedded = new(() =>
    {
        using var stream = typeof(FdaSupplementCombinations).Assembly.GetManifestResourceStream("RATools.FdaSupplementCombinations")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        return new(bytes.ToArray());
    });
    // Generation validates the versioned writer's supported values; inspection must use FromSnapshot.
    internal static FdaSupplementCombinations WriterPolicy => Embedded.Value;
    private FdaSupplementCombinations(byte[] bytes)
    {
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), Digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported supplement mapping bytes.");
        using var json = JsonDocument.Parse(bytes);
        _rows = json.RootElement.GetProperty("combinations").EnumerateArray().ToDictionary(
            item => item.GetProperty("submissionType").GetString()!,
            item => item.GetProperty("effectiveTypes").EnumerateArray().Select(value => value.GetString()!).ToArray(), StringComparer.Ordinal);
    }
    internal IReadOnlyList<string> ForType(string type) => _rows.TryGetValue(type, out var values) ? Array.AsReadOnly(values) : [];
    internal IEnumerable<string> Types => _rows.Keys;
    internal bool Allows(string type, string effective) => ForType(type).Contains(effective, StringComparer.Ordinal);
    internal static FdaSupplementCombinations? FromSnapshot(StandardsProfileSnapshot snapshot)
    {
        var resource = snapshot.Resources.SingleOrDefault(item => item.Key == ResourceKey);
        var rules = snapshot.Resources.SingleOrDefault(item => item.Key == "rules");
        if (resource is null || resource.Sha256 != Digest || resource.Category != "RuleMapping" || rules is null) return null;
        using var stream = new MemoryStream(rules.ReadBytes()); var catalog = PackageValidationCatalog.Load(stream);
        if (!catalog.Sources.TryGetValue("ratools-fda-supplement-combinations-v1", out var source) ||
            source.Kind != RuleSourceKind.ProjectPolicy || source.Version != "fda-supplement-combinations-v1" || source.Sha256 != Digest || source.VerificationStatus != "Verified" ||
            FdaM1ConditionalPolicy.FromSnapshot(snapshot, catalog) is null) return null;
        return new(resource.ReadBytes());
    }
    internal static bool Supports(ValidationRuleDescriptor rule) => rule.InternalRuleId == RuleId && rule.AuthorityRuleId is null &&
        rule.SourceId == "ratools-fda-supplement-combinations-v1" && rule.RuleVersion == "1";

    internal void Inspect(ParsedBackbone document, FdaM1ConditionalPolicy conditional,
        Action<ParsedXmlElement, string, string> fail, Action<ParsedXmlElement, string, string> incomplete)
    {
        var elements = document.Elements.ToDictionary(item => item.NodePath, StringComparer.Ordinal);
        IEnumerable<ParsedXmlElement> Children(ParsedXmlElement parent, string name) => parent.Children.Select(path => elements[path]).Where(item => item.LocalName == name && item.NamespaceUri.Length == 0);
        foreach (var application in document.Elements.Take(1).SelectMany(root => Children(root, "admin")).SelectMany(admin => Children(admin, "application-set")).SelectMany(set => Children(set, "application")))
        {
            var parents = Children(application, "submission-information").ToArray();
            var ids = parents.SelectMany(parent => Children(parent, "submission-id")).ToArray();
            var numbers = parents.SelectMany(parent => Children(parent, "sequence-number")).ToArray();
            if (parents.Length != 1 || ids.Length != 1 || numbers.Length != 1)
            { incomplete(application, "submission-information", "Unambiguous per-application metadata is required for supplement combinations."); continue; }
            var id = ids[0]; var type = FdaM1AttributePolicy.Attribute(id, "submission-type");
            var effective = FdaM1AttributePolicy.Attribute(id, "supplement-effective-date-type");
            var required = conditional.RequiresEffectiveType(type, FdaM1AttributePolicy.Attribute(numbers[0], "submission-sub-type"));
            if (required is null || required == true && string.IsNullOrWhiteSpace(effective))
                incomplete(id, "@supplement-effective-date-type", "Explicit supported inputs are required to establish supplement combination coverage.");
            else if (effective is not null && (required == false || !Allows(type!, effective)))
                fail(id, "@supplement-effective-date-type", "This effective date type is not permitted for the selected supplement and submission subtype by M1 Tables 2/3.");
        }
    }
}
