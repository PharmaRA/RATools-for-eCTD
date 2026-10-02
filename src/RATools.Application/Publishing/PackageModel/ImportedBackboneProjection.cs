using System.Xml;
using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Standards;

namespace RATools.Application.Publishing.PackageModel;

internal static class ImportedBackboneProjection
{
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
