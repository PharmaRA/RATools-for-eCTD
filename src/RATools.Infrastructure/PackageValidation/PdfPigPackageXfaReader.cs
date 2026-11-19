using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using RATools.Application.PackageValidation;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

internal sealed class PdfPigPackageXfaReader(Func<IToken?, IToken?> tokenResolver, Action<int> count, PackageReadLimits limits)
{
    private const string DataNamespace = "http://www.xfa.org/schema/xfa-data/1.0/";
    private const string XdpNamespace = "http://ns.adobe.com/xdp/";
    private readonly List<PackageXfaPacket> _packets = [];
    private readonly List<PackageXfaDataNode> _data = [];
    private readonly List<string> _reasons = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private long _bytes;
    private int _xmlNodes;
    private int _templates;
    private int _datasets;
    private int _resolutionFailures;

    private IToken? Resolve(IToken? token)
    {
        var value = tokenResolver(token);
        if (token is not null && value is null)
        { _resolutionFailures++; Unknown("Unresolved XFA object or stream metadata."); }
        return value;
    }

    public PackageXfaFacts Read(IToken token)
    {
        count(0);
        var value = Resolve(token);
        if (value is ArrayToken array)
        {
            byte[]? header = null, trailer = null;
            if (array.Length % 2 != 0) Unknown("XFA packet array has an unmatched name or stream.");
            for (var index = 0; index + 1 < array.Length; index += 2)
            {
                count(0);
                var name = Resolve(array.Data[index]) switch { StringToken text => text.Data, HexToken hex => hex.Data, _ => null };
                if (string.IsNullOrEmpty(name)) { Unknown("XFA packet name is invalid."); continue; }
                if (!_names.Add(name)) Unknown("Duplicate XFA packet name.");
                var bytes = Decode(array.Data[index + 1]);
                if (bytes is null) continue;
                _packets.Add(new(name, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength));
                if (name == "xdp:xdp")
                {
                    if (index != 0) Unknown("XDP envelope header must be the first packet.");
                    header = bytes;
                    continue;
                }
                if (name == "</xdp:xdp>")
                {
                    if (index != array.Length - 2) Unknown("XDP envelope trailer must be the last packet.");
                    trailer = bytes;
                    continue;
                }
                Parse(bytes, name);
            }
            if (header is not null || trailer is not null)
            {
                if (header is null || trailer is null) Unknown("XDP envelope requires both header and trailer.");
                else CheckEnvelope(header, trailer);
            }
        }
        else if (value is StreamToken)
        {
            if (Decode(value) is { } bytes)
            {
                _packets.Add(new("xdp", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength));
                Parse(bytes, "xdp");
            }
        }
        else Unknown("XFA must be a packet array or an XDP stream.");
        if (_templates != 1 || _datasets != 1) Unknown("Exactly one readable template and datasets packet are required.");
        count(0);
        return new(_reasons.Count == 0, _packets, _data, _reasons);
    }

    private byte[]? Decode(IToken token)
    {
        count(0);
        if (Resolve(token) is not StreamToken stream) { Unknown("XFA packet is not a readable stream."); return null; }
        var before = _resolutionFailures;
        var filter = Resolve(Get(stream.StreamDictionary, "Filter"));
        if (filter is ArrayToken filters)
        {
            if (filters.Length != 1) { Unknown("XFA stream filter chain is not supported."); return null; }
            filter = Resolve(filters.Data[0]);
        }
        var flate = filter is NameToken { Data: "FlateDecode" or "Fl" };
        if (filter is not (null or NullToken) && !flate) { Unknown("XFA stream filter is not supported."); return null; }
        var parameters = Resolve(Get(stream.StreamDictionary, "DecodeParms"));
        if (parameters is ArrayToken { Length: 1 } array) parameters = Resolve(array.Data[0]);
        if (_resolutionFailures != before) return null;
        if (parameters is not (null or NullToken)) { Unknown("XFA stream predictor/parameters are not supported."); return null; }
        try
        {
            var encoded = stream.Data.ToArray();
            if (flate && (encoded.Length < 6 || (encoded[0] & 15) != 8 || (encoded[0] >> 4) > 7 ||
                ((encoded[0] << 8) + encoded[1]) % 31 != 0 || (encoded[1] & 32) != 0))
            { Unknown("XFA ZLib header or required trailer is invalid."); return null; }
            using var compressed = new MemoryStream(encoded, writable: false);
            // Prevent inflater read-ahead from concealing trailing bytes after the ZLib stream.
            using var exact = flate ? new ExactReadStream(compressed, count) : null;
            using var inflated = flate ? new ZLibStream(exact!, CompressionMode.Decompress, leaveOpen: true) : null;
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            uint adlerA = 1, adlerB = 0;
            Stream source = inflated is null ? compressed : inflated;
            while (true)
            {
                count(0);
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                _bytes += read;
                if (_bytes > limits.MaxXmlCharacters || _bytes > limits.MaxExpandedBytes ||
                    flate && output.Length + read > Math.Max(1L, compressed.Length) * limits.MaxCompressionRatio)
                    throw new PdfInspectionLimitException("XFA decoded byte/compression budget exceeded.");
                if (flate)
                    for (var index = 0; index < read; index++)
                    { adlerA = (adlerA + buffer[index]) % 65521; adlerB = (adlerB + adlerA) % 65521; }
                output.Write(buffer, 0, read);
            }
            if (flate && (compressed.Position != compressed.Length ||
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(encoded.Length - 4)) != (adlerB << 16 | adlerA)))
            { Unknown("XFA ZLib trailer is truncated, inconsistent or followed by trailing bytes."); return null; }
            return output.ToArray();
        }
        catch (InvalidDataException) { Unknown("XFA compressed data could not be decoded."); return null; }
    }

    private sealed class ExactReadStream(Stream inner, Action<int> count) : Stream
    {
        private int _reads;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int length) { Check(length); return inner.Read(buffer, offset, Math.Min(1, length)); }
        public override int Read(Span<byte> buffer) { Check(buffer.Length); return inner.Read(buffer[..Math.Min(1, buffer.Length)]); }
        private void Check(int length)
        {
            if ((_reads++ & 4095) == 0) count(0);
            // A finished inflater stops reading at the trailer. A further source read is
            // truncation, not a normal EOF that ZLibStream may silently accept.
            if (length > 0 && inner.Position == inner.Length) throw new InvalidDataException("ZLib stream did not finish before input exhaustion.");
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int length) => throw new NotSupportedException();
    }

    private void CheckEnvelope(byte[] header, byte[] trailer)
    {
        try
        {
            // Insert one marker to prove the header leaves precisely the XDP root open,
            // and the trailer closes it without adding unrelated content.
            var marker = Encoding.UTF8.GetBytes("<boundary xmlns='urn:ratools:xfa-envelope-check'/>");
            using var source = new MemoryStream();
            source.Write(header); source.Write(marker); source.Write(trailer); source.Position = 0;
            using var reader = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = limits.MaxXmlCharacters, MaxCharactersFromEntities = 1
            });
            var roots = 0;
            var markers = 0;
            while (reader.Read())
            {
                count(0);
                if (++_xmlNodes > limits.MaxXmlNodes || reader.Depth > limits.MaxXmlDepth)
                    throw new PdfInspectionLimitException("XFA envelope node/depth budget exceeded.");
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Depth == 0 && reader.LocalName == "xdp" && reader.NamespaceURI == XdpNamespace && !reader.IsEmptyElement) roots++;
                    else if (reader.Depth == 1 && reader.LocalName == "boundary" && reader.NamespaceURI == "urn:ratools:xfa-envelope-check" && reader.IsEmptyElement) markers++;
                    else Unknown("XDP envelope contains unexpected content.");
                }
                else if (reader.NodeType is not (XmlNodeType.EndElement or XmlNodeType.XmlDeclaration or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
                    Unknown("XDP envelope contains text, comments or instructions instead of packet boundaries.");
            }
            if (roots != 1 || markers != 1) Unknown("XDP envelope does not enclose the packets exactly once.");
        }
        catch (XmlException) { Unknown("XDP envelope is malformed or unsafe."); }
    }

    private sealed class Frame(string name, string ns, string path, bool capture)
    {
        public string Name { get; } = name;
        public string Namespace { get; } = ns;
        public string Path { get; } = path;
        public bool Capture { get; } = capture;
        public bool HasChildren { get; set; }
        public StringBuilder Text { get; } = new();
        public Dictionary<string, int> Children { get; } = new(StringComparer.Ordinal);
    }

    private void Parse(byte[] bytes, string packet)
    {
        var stack = new Stack<Frame>();
        var datasetDepth = -1;
        var dataDepth = -1;
        var dataRoots = 0;
        try
        {
            using var source = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = limits.MaxXmlCharacters, MaxCharactersFromEntities = 1,
                IgnoreWhitespace = false, IgnoreComments = false
            });
            while (reader.Read())
            {
                count(0);
                if (++_xmlNodes > limits.MaxXmlNodes || reader.Depth > limits.MaxXmlDepth)
                    throw new PdfInspectionLimitException("XFA XML node/depth budget exceeded.");
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var depth = stack.Count;
                    if (depth == 0)
                    {
                        if (packet == "xdp" && !(reader.LocalName == "xdp" && reader.NamespaceURI == XdpNamespace)) Unknown("Invalid XDP root.");
                        if (packet == "template" && !IsTemplate(reader)) Unknown("Invalid XFA template root.");
                        if (packet == "datasets" && !(reader.LocalName == "datasets" && reader.NamespaceURI == DataNamespace)) Unknown("Invalid XFA datasets root.");
                    }
                    var packetRoot = packet == "xdp" ? depth == 1 && stack.Peek().Name == "xdp" && stack.Peek().Namespace == XdpNamespace : depth == 0;
                    if (packetRoot && (packet == "template" || packet == "xdp") && IsTemplate(reader)) _templates++;
                    if (packetRoot && (packet == "datasets" || packet == "xdp") && reader.LocalName == "datasets" && reader.NamespaceURI == DataNamespace)
                    { _datasets++; datasetDepth = depth; dataRoots = 0; }
                    if (datasetDepth >= 0 && depth == datasetDepth + 1)
                    {
                        if (reader.LocalName == "data" && reader.NamespaceURI == DataNamespace)
                        { dataRoots++; dataDepth = depth; }
                        else Unknown("Unexpected datasets child.");
                    }
                    var key = "{" + Uri.EscapeDataString(reader.NamespaceURI) + "}" + reader.LocalName;
                    var parent = stack.Count == 0 ? null : stack.Peek();
                    var occurrence = 1;
                    if (parent is not null)
                    {
                        parent.HasChildren = true;
                        occurrence = parent.Children.GetValueOrDefault(key) + 1;
                        parent.Children[key] = occurrence;
                    }
                    var path = (parent?.Path ?? "") + "/" + key + "[" + occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                    var frame = new Frame(reader.LocalName, reader.NamespaceURI, path, dataDepth >= 0 && depth > dataDepth);
                    stack.Push(frame);
                    if (reader.IsEmptyElement) End();
                }
                else if (reader.NodeType == XmlNodeType.EndElement) End();
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    if (stack.TryPeek(out var frame) && frame.Capture) frame.Text.Append(reader.Value);
                }
            }
        }
        catch (XmlException) { Unknown("XFA XML was malformed, unsafe or exceeded its XML character limit."); }

        void End()
        {
            var frame = stack.Pop();
            if (frame.Capture) _data.Add(new(frame.Path, frame.Name, frame.Namespace, frame.Text.ToString(), frame.HasChildren));
            if (stack.Count == dataDepth) dataDepth = -1;
            if (stack.Count == datasetDepth)
            {
                if (dataRoots != 1) Unknown("Datasets must contain exactly one data element.");
                datasetDepth = -1;
            }
        }
    }

    private static bool IsTemplate(XmlReader reader) => reader.LocalName == "template" &&
        reader.NamespaceURI.StartsWith("http://www.xfa.org/schema/xfa-template/", StringComparison.Ordinal);
    private void Unknown(string reason) { if (!_reasons.Contains(reason, StringComparer.Ordinal)) _reasons.Add(reason); }
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var token) ? token : null;
}
