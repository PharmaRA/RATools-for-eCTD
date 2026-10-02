using System.Text;
using System.Xml;
using System.Xml.Schema;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal sealed class XmlInspectionLimitException(string message) : Exception(message);

internal sealed class XmlInspectionFindings(PackageReadLimits limits)
{
    private readonly List<ValidationFinding> _items = [];
    private int _nodes;
    public IReadOnlyList<ValidationFinding> Items => _items;
    public bool Truncated { get; private set; }
    public bool StructureLimitReached { get; private set; }

    public void CountNode(int depth, ValidationLocation location)
    {
        if (_nodes >= limits.MaxXmlNodes || depth > limits.MaxXmlDepth)
        {
            StructureLimitReached = true;
            Add(new("XML-LIMITS", CheckStatus.Fail, ValidationSeverity.Error, "XML_STRUCTURE_LIMIT", "XML node count or depth exceeds the configured limit.", location));
            throw new XmlInspectionLimitException("XML structure limit exceeded.");
        }
        _nodes++;
    }

    public void Add(ValidationFinding finding)
    {
        if (_items.Count >= limits.MaxXmlFindings - 1)
        {
            if (!Truncated) _items.Add(new("XML-LIMITS", CheckStatus.Fail, ValidationSeverity.Error, "XML_FINDING_LIMIT",
                "The inspection stopped at the configured finding limit; remaining checks are incomplete.", finding.Location));
            Truncated = true;
            throw new XmlInspectionLimitException("XML finding limit exceeded.");
        }
        _items.Add(finding);
    }
}

internal sealed record ReadXmlDocument(bool Complete, string? DocumentTypeName, string? SystemId, string? PublicId,
    IReadOnlyList<ParsedXmlElement> Elements, IReadOnlyList<string> ResolvedAssets, IReadOnlyList<ParsedXmlProcessingInstruction> ProcessingInstructions);

internal static class PackageXmlDocumentReader
{
    public static async Task<ReadXmlDocument> ReadAsync(ICapturedPackageInput input, string logicalPath,
        PackageXmlAssets assets, PackageXmlBackboneProfile profile, XmlInspectionFindings findings, CancellationToken cancellationToken)
    {
        var builders = new List<ElementBuilder>();
        var instructions = new List<ParsedXmlProcessingInstruction>();
        var stack = new Stack<ElementBuilder>();
        var sequence = input.Manifest.SequenceNumber;
        var relative = logicalPath[(sequence.Length + 1)..];
        ValidationLocation Location(int? line = null, int? column = null, string? path = null) =>
            new(sequence, logicalPath, relative, NodePath: path ?? (stack.TryPeek(out var current) ? current.Path : null),
                Line: line is > 0 ? line : null, Column: column is > 0 ? column : null);
        var resolver = new PackageXmlResolver(assets, profile, logicalPath);
        var settings = new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Parse, ValidationType = ValidationType.DTD,
            XmlResolver = resolver, MaxCharactersInDocument = input.Limits.MaxXmlCharacters,
            MaxCharactersFromEntities = input.Limits.MaxXmlEntityCharacters,
            IgnoreComments = false, IgnoreWhitespace = false, CloseInput = true
        };
        settings.ValidationEventHandler += (_, args) => findings.Add(new(profile.RuleId,
            args.Severity == XmlSeverityType.Error ? CheckStatus.Fail : CheckStatus.NotEvaluated,
            args.Severity == XmlSeverityType.Error ? ValidationSeverity.Error : ValidationSeverity.Warning,
            "DTD_VALIDATION_ERROR", args.Message, Location(args.Exception?.LineNumber, args.Exception?.LinePosition)));
        string? documentType = null, systemId = null, publicId = null;
        var complete = false;
        try
        {
            using var reader = XmlReader.Create(new CancellationInputStream(input.OpenRead(logicalPath), cancellationToken), settings,
                PackageXmlResolver.DocumentUri(logicalPath).AbsoluteUri);
            var lineInfo = (IXmlLineInfo)reader;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = Location(lineInfo.LineNumber, lineInfo.LinePosition);
                findings.CountNode(reader.Depth + 1, location);
                if (reader.NodeType == XmlNodeType.DocumentType)
                {
                    documentType = reader.Name;
                    systemId = reader.GetAttribute("SYSTEM");
                    publicId = reader.GetAttribute("PUBLIC");
                    if (reader.Value.Length != 0) throw new UntrustedXmlResourceException("Internal DTD declarations are not allowed in a pinned-profile input.");
                    if (documentType != profile.DocumentTypeName)
                    {
                        findings.Add(new("PROFILE-VERSION", CheckStatus.Fail, ValidationSeverity.Error, "DOCTYPE_PROFILE_MISMATCH",
                            "The document type name differs from the explicitly selected profile.", location, profile.DocumentTypeName, documentType));
                        break;
                    }
                    var resolved = resolver.ResolveUri(PackageXmlResolver.DocumentUri(logicalPath), systemId);
                    var expected = PackageXmlResolver.DocumentUri(sequence + "/" + assets.Assets[profile.MainAssetId].LogicalPath);
                    if (resolved != expected)
                    {
                        findings.Add(new("PROFILE-VERSION", CheckStatus.Fail, ValidationSeverity.Error, "DTD_PROFILE_MISMATCH",
                            "The declared DTD is not this backbone's pinned main DTD.", location, expected.AbsoluteUri, resolved.AbsoluteUri));
                        break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    if (builders.Count == 0 && (documentType is null || reader.Name != profile.DocumentTypeName ||
                        reader.LocalName != profile.RootLocalName || reader.NamespaceURI != profile.NamespaceUri || reader.GetAttribute("dtd-version") != profile.DtdVersion))
                    {
                        findings.Add(new("PROFILE-VERSION", CheckStatus.Fail, ValidationSeverity.Error, "BACKBONE_PROFILE_MISMATCH",
                            "DOCTYPE, root name, namespace and effective DTD version must match the selected profile.", location,
                            $"{profile.DocumentTypeName}; {profile.NamespaceUri}; {profile.DtdVersion}", $"{documentType}; {reader.Name}; {reader.NamespaceURI}; {reader.GetAttribute("dtd-version")}"));
                        break;
                    }
                    var parent = stack.TryPeek(out var current) ? current : null;
                    var ordinal = parent?.NextOrdinal(reader.Name) ?? 1;
                    var path = (parent?.Path ?? "") + "/" + reader.Name + "[" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                    var node = new ElementBuilder(path, parent?.Path, reader.Name, reader.LocalName, reader.NamespaceURI,
                        location with { NodePath = path });
                    var elementDepth = reader.Depth + 1;
                    while (reader.MoveToNextAttribute())
                    {
                        findings.CountNode(elementDepth, location with { NodePath = path, FieldPath = "@" + reader.Name });
                        node.Attributes.Add(new(reader.Name, reader.LocalName, reader.NamespaceURI, reader.Value, reader.IsDefault, lineInfo.LineNumber, lineInfo.LinePosition));
                    }
                    reader.MoveToElement();
                    builders.Add(node);
                    parent?.Children.Add(path);
                    if (!reader.IsEmptyElement) stack.Push(node);
                }
                else if (reader.NodeType == XmlNodeType.EndElement) stack.Pop();
                else if (reader.NodeType == XmlNodeType.ProcessingInstruction)
                    instructions.Add(new(reader.Name, reader.Value, location));
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
                {
                    if (stack.TryPeek(out var parent)) parent.Text.Append(reader.Value);
                }
            }
            complete = reader.EOF && documentType is not null && builders.Count > 0 && stack.Count == 0;
        }
        catch (XmlInspectionLimitException) { }
        catch (XmlException exception)
        {
            var untrusted = HasUntrustedCause(exception);
            var quota = exception.Message.Contains("MaxCharacters", StringComparison.Ordinal);
            TryAdd(new(untrusted ? "XML-OFFLINE" : quota ? "XML-LIMITS" : profile.RuleId,
                CheckStatus.Fail, ValidationSeverity.Error, untrusted ? "UNTRUSTED_XML_RESOURCE" : quota ? "XML_CHARACTER_LIMIT" : "XML_NOT_WELL_FORMED",
                exception.Message, Location(exception.LineNumber, exception.LinePosition)));
        }
        catch (IOException exception)
        {
            TryAdd(new(profile.RuleId, CheckStatus.Fail, ValidationSeverity.Error, "BACKBONE_READ_FAILED", exception.Message, Location()));
        }
        return new(complete, documentType, systemId, publicId, Array.AsReadOnly(builders.Select(builder =>
            { cancellationToken.ThrowIfCancellationRequested(); return builder.Freeze(); }).ToArray()),
            Array.AsReadOnly(resolver.ResolvedAssets.ToArray()), Array.AsReadOnly(instructions.ToArray()));

        void TryAdd(ValidationFinding finding)
        {
            try { findings.Add(finding); }
            catch (XmlInspectionLimitException) { }
        }
    }

    private static bool HasUntrustedCause(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is UntrustedXmlResourceException) return true;
            exception = exception.InnerException;
        }
        return false;
    }

    private sealed class ElementBuilder(string path, string? parentPath, string name, string localName, string namespaceUri, ValidationLocation location)
    {
        private readonly Dictionary<string, int> _ordinals = new(StringComparer.Ordinal);
        public string Path { get; } = path;
        public List<ParsedXmlAttributeValue> Attributes { get; } = [];
        public List<string> Children { get; } = [];
        public StringBuilder Text { get; } = new();
        public int NextOrdinal(string childName) => _ordinals[childName] = _ordinals.GetValueOrDefault(childName) + 1;
        public ParsedXmlElement Freeze() => new(Path, parentPath, name, localName, namespaceUri, Attributes, Children, Text.ToString(), location);
    }

    private sealed class CancellationInputStream(Stream inner, CancellationToken token) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { token.ThrowIfCancellationRequested(); return inner.Read(buffer, offset, count); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, token);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, token);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
