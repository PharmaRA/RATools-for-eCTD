using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace RATools.Application.PackageValidation;

internal static class PackageDefineXmlReader
{
    private static readonly XNamespace Odm = "http://www.cdisc.org/ns/odm/v1.3";
    private static readonly XNamespace Define = "http://www.cdisc.org/ns/def/v2.1";
    private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";

    internal static PackageDefineXmlFacts Read(ReadOnlyMemory<byte> input, string expectedSha256,
        DefineXmlReadLimits? limits = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        limits ??= new();
        if (limits.MaxBytes < 0 || limits.MaxNodes < 0 || limits.MaxDepth < 0 || limits.MaxCharacters < 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
        if (input.Length > limits.MaxBytes) return Result(DefineXmlReadStatus.LimitExceeded);
        var bytes = input.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(sha256, expectedSha256, StringComparison.Ordinal)) return Result(DefineXmlReadStatus.InputMismatch, sha256);
        var nodes = limits.MaxNodes;
        var characters = limits.MaxCharacters;
        try
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CheckCharacters = true,
                ConformanceLevel = ConformanceLevel.Document
            });
            XElement? root = null;
            var stack = new Stack<XElement>();
            var unsupportedBase = false;
            while (reader.Read())
            {
                Spend(reader.Name.Length + (long)reader.Value.Length);
                if (reader.Depth + 1 > limits.MaxDepth) throw new DefineLimitException();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var element = new XElement(XName.Get(reader.LocalName, reader.NamespaceURI));
                    if (root is null) root = element;
                    else if (stack.TryPeek(out var parent)) parent.Add(element);
                    else return Result(DefineXmlReadStatus.Invalid, sha256);
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            Spend(reader.Name.Length + (long)reader.Value.Length);
                            if (reader.NamespaceURI == XNamespace.Xml.NamespaceName && reader.LocalName == "base") unsupportedBase = true;
                            if (reader.Prefix != "xmlns" && reader.Name != "xmlns")
                                element.Add(new XAttribute(XName.Get(reader.LocalName, reader.NamespaceURI), reader.Value));
                        } while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                    if (!reader.IsEmptyElement) stack.Push(element);
                }
                else if (reader.NodeType == XmlNodeType.EndElement) stack.Pop();
                // Text, comments and processing instructions consume budget but cannot supply declarations.
            }
            token.ThrowIfCancellationRequested();
            if (root is null) return Result(DefineXmlReadStatus.Invalid, sha256);
            if (root.Name != Odm + "ODM" || unsupportedBase) return Result(DefineXmlReadStatus.Unsupported, sha256);
            var studies = root.Elements(Odm + "Study").Take(2).ToArray();
            if (studies.Length != 1) return Result(DefineXmlReadStatus.Unsupported, sha256);
            var versions = studies[0].Elements(Odm + "MetaDataVersion").Take(2).ToArray();
            if (versions.Length != 1) return Result(DefineXmlReadStatus.Unsupported, sha256);
            var metadata = versions[0];
            if (Attribute(metadata, Define + "DefineVersion") != "2.1.0" || metadata.Elements(Odm + "Include").Any())
                return Result(DefineXmlReadStatus.Unsupported, sha256);
            var containers = metadata.Elements(Define + "Standards").Take(2).ToArray();
            if (containers.Length > 1) return Result(DefineXmlReadStatus.Unsupported, sha256);
            var standards = new List<DefineStandardDeclaration>();
            foreach (var standard in containers.SelectMany(container => container.Elements(Define + "Standard")))
            {
                token.ThrowIfCancellationRequested();
                standards.Add(new(Attribute(standard, "OID"), Attribute(standard, "Name"), Attribute(standard, "Type"),
                    Attribute(standard, "Version"), Attribute(standard, "Status")));
            }
            var groups = metadata.Elements(Odm + "ItemGroupDef").ToArray();
            var leaves = new List<DefineLeafDeclaration>();
            foreach (var leaf in metadata.Elements(Define + "leaf").Concat(groups.SelectMany(group => group.Elements(Define + "leaf"))))
            {
                token.ThrowIfCancellationRequested();
                leaves.Add(new(Attribute(leaf, "ID"), Attribute(leaf, Xlink + "href")));
            }
            var standardIndex = Index(standards, standard => standard.Oid);
            var leafIndex = Index(leaves, leaf => leaf.Id);
            var datasets = new List<DefineDatasetDeclaration>();
            foreach (var group in groups)
            {
                token.ThrowIfCancellationRequested();
                var standardOid = Attribute(group, Define + "StandardOID");
                var archive = Attribute(group, Define + "ArchiveLocationID");
                var standard = Resolve(standardOid, standardIndex);
                var file = Resolve(archive, leafIndex);
                datasets.Add(new(Attribute(group, "OID"), Attribute(group, "Name"), Attribute(group, "SASDatasetName"),
                    Attribute(group, "Domain"), standardOid, archive, Attribute(group, Define + "IsNonStandard"),
                    Attribute(group, Define + "HasNoData"), standard.Status, standard.Value, file.Status, file.Value));
            }
            token.ThrowIfCancellationRequested();
            return new(DefineXmlReadStatus.Read, sha256, true, Attribute(studies[0], "OID"), Attribute(metadata, "OID"),
                Attribute(metadata, Define + "DefineVersion"), standards, leaves, datasets);
        }
        catch (DefineLimitException) { return Result(DefineXmlReadStatus.LimitExceeded, sha256); }
        catch (XmlException) { return Result(DefineXmlReadStatus.Invalid, sha256); }

        void Spend(long count)
        {
            token.ThrowIfCancellationRequested();
            if (nodes-- <= 0 || count > characters) throw new DefineLimitException();
            characters -= count;
        }
        Dictionary<string, List<T>> Index<T>(IEnumerable<T> values, Func<T, string?> key)
        {
            var index = new Dictionary<string, List<T>>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                token.ThrowIfCancellationRequested();
                if (key(value) is not { Length: > 0 } id) continue;
                if (!index.TryGetValue(id, out var entries)) index.Add(id, entries = []);
                entries.Add(value);
            }
            return index;
        }
        PackageDefineXmlFacts Result(DefineXmlReadStatus status, string? hash = null)
        { token.ThrowIfCancellationRequested(); return new(status, hash); }
    }

    private static string? Attribute(XElement element, XName name) => (string?)element.Attribute(name);
    private static (DefineReferenceStatus Status, T? Value) Resolve<T>(string? key, Dictionary<string, List<T>> index) where T : class
    {
        if (key is null) return (DefineReferenceStatus.Unspecified, null);
        if (!index.TryGetValue(key, out var values)) return (DefineReferenceStatus.Missing, null);
        return values.Count == 1 ? (DefineReferenceStatus.Resolved, values[0]) : (DefineReferenceStatus.Ambiguous, null);
    }
    private sealed class DefineLimitException : Exception;
}
