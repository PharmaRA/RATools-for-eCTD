using System.Xml;
using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Standards;
using RATools.Application.Publishing.EuRegional;
using System.Text;

namespace RATools.Application.Publishing.PackageModel;

internal static class ImportedBackboneProjection
{
    public static Dictionary<(string BackbonePath, string LeafId), (int WrapperOrdinal, int TraversalOrdinal)> ReadEuLeafOrder(
        IReadOnlyList<ImportedBackbone> sources, string sequenceNumber)
    {
        const long maximumCharacters = 8 * 1024 * 1024;
        var result = new Dictionary<(string BackbonePath, string LeafId), (int WrapperOrdinal, int TraversalOrdinal)>();
        if (sources.Count > 64) throw ArchiveError("importedBackbones", "exceed the bounded source inventory");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0; var wrapperOrdinal = 0; var traversalOrdinal = 0;
        foreach (var source in sources)
        {
            if (source.SequenceNumber != sequenceNumber || !paths.Add(source.RelativePath))
                throw ArchiveError("importedBackbones", "must use distinct paths within the exact sequence");
            if (source.RelativePath == "index.xml") continue;
            if (source.Xml.Length > maximumCharacters)
                throw ArchiveError("importedBackbones", "exceed the bounded EU source XML budget");
            bytes += Encoding.UTF8.GetByteCount(source.Xml);
            if (bytes > maximumCharacters)
                throw ArchiveError("importedBackbones", "exceed the bounded EU source XML budget");
            XDocument document;
            try
            {
                using var reader = XmlReader.Create(new StringReader(source.Xml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = maximumCharacters });
                document = XDocument.Load(reader);
            }
            catch (XmlException)
            { throw ArchiveError("importedBackbones", "must contain bounded well-formed archival XML"); }
            var wrappers = new Dictionary<XElement, int>();
            foreach (var leaf in document.Descendants().Where(element => element.Name.LocalName == "leaf"))
            {
                var id = leaf.Attribute("ID")?.Value;
                if (string.IsNullOrWhiteSpace(id)) throw ArchiveError("importedBackbones", "must identify every archived leaf explicitly");
                // Preserve source parent identity, even when two wrappers have equal or
                // subsequently edited attributes. Unknown XML is never emitted again.
                var wrapper = leaf.Ancestors().FirstOrDefault(element => element.Name.LocalName is "specific" or "pi-doc") ?? leaf.Parent!;
                if (!wrappers.TryGetValue(wrapper, out var ordinal)) { ordinal = wrapperOrdinal++; wrappers.Add(wrapper, ordinal); }
                if (!result.TryAdd((source.RelativePath, id), (ordinal, traversalOrdinal++)))
                    throw ArchiveError("importedBackbones", "contain a duplicate leaf ID within one backbone path");
            }
        }
        return result;
    }

    private static EuRegionalXmlWriterException ArchiveError(string field, string reason) => new($"{field}: {reason}.", field);

    public static (BackboneXmlProfile Profile, IReadOnlyList<EctdBackboneReference> References) Create(
        BackboneXmlProfile profile, IReadOnlyList<ImportedBackbone> sources, string sequenceNumber)
    {
        var regionalSources = sources.Where(source => source.RelativePath != "index.xml").ToArray();
        if (regionalSources.Length > 1) throw new EctdPackageNodeException("RegionalProfileRequired", "Multiple regional backbones require an applicable regional output profile.");
        var path = regionalSources.SingleOrDefault()?.RelativePath ?? profile.Regional.RelativePath!;
        if (path != profile.Regional.RelativePath)
            profile = profile with { Regional = profile.Regional with
            {
                RelativePath = path,
                DtdSystemId = string.Concat(Enumerable.Repeat("../", path.Count(character => character == '/'))) +
                    "util/dtd/" + Path.GetFileName(profile.Regional.DtdSystemId)
            }};
        XElement? original = null;
        if (sources.SingleOrDefault(source => source.RelativePath == "index.xml") is { } index)
        {
            using var reader = XmlReader.Create(new StringReader(index.Xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            var xml = XDocument.Load(reader);
            var container = xml.Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "m1-administrative-information-and-prescribing-information");
            if (container?.Elements("leaf").Any(leaf => leaf.Attribute("operation")?.Value != "new") == true)
                throw new EctdPackageNodeException("RegionalReferenceMustBeNew", "ICH requires a regional backbone reference to use operation new.");
            original = container?.Elements().SingleOrDefault(element => element.Name.LocalName == "leaf" && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "href" && Uri.TryCreate(new Uri("https://ectd.invalid/"), attribute.Value, out var target) &&
                    Uri.UnescapeDataString(target.AbsolutePath.TrimStart('/')) == path));
        }
        return (profile, [new EctdBackboneReference(path, original?.Attribute("ID")?.Value ?? $"regional-{sequenceNumber}",
            original?.Elements().FirstOrDefault(element => element.Name.LocalName == "title")?.Value ?? "Regional backbone",
            original?.Attribute("operation")?.Value ?? "new", original?.Attribute("modified-file")?.Value)]);
    }
}
