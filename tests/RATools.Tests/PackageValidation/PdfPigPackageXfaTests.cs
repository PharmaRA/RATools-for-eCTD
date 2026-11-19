using RATools.Application.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PdfPigPackageXfaTests
{
    private const string Template = "<template xmlns='http://www.xfa.org/schema/xfa-template/3.3/'><subform name='form'/></template>";
    private const string DataStart = "<xfa:datasets xmlns:xfa='http://www.xfa.org/schema/xfa-data/1.0/'><xfa:data>";
    private const string DataEnd = "</xfa:data></xfa:datasets>";

    [Theory]
    [InlineData("<form><number>000123</number></form>", "000123")]
    [InlineData("<form><number/></form>", "")]
    public void PacketReadCompletenessIsDistinctFromFormQualification(string data, string value)
    {
        var result = Inspect("/XFA [(template) 7 0 R (datasets) 8 0 R]", Stream(Template), Stream(DataStart + data + DataEnd));
        Assert.True(result.Form.HasXfa);
        Assert.False(result.Form.Complete);
        Assert.True(result.Form.Xfa.ReadComplete, string.Join(" ", result.Form.Xfa.IncompleteReasons));
        Assert.Equal(value, Assert.Single(result.Form.Xfa.Data, node => node.LocalName == "number").DirectText);
        Assert.Equal(2, result.Form.Xfa.Packets.Count);
        Assert.All(result.Form.Xfa.Packets, packet => Assert.Equal(64, packet.Sha256.Length));
    }

    [Fact]
    public void EmptyDatasetsIsDifferentFromMissingDatasets()
    {
        var empty = Inspect("/XFA [(template) 7 0 R (datasets) 8 0 R]", Stream(Template), Stream(DataStart + DataEnd)).Form.Xfa;
        Assert.True(empty.ReadComplete);
        Assert.Empty(empty.Data);
        var missing = Inspect("/XFA [(template) 7 0 R]", Stream(Template)).Form.Xfa;
        Assert.False(missing.ReadComplete);
        Assert.Empty(missing.Data);
    }

    [Fact]
    public void RepeatedDataNamesRetainDistinctQualifiedPaths()
    {
        var data = DataStart + "<form><number>123456</number><number>654321</number><n:number xmlns:n='urn:other'>other</n:number></form>" + DataEnd;
        var result = Inspect("/XFA [(template) 7 0 R (datasets) 8 0 R]", Stream(Template), Stream(data)).Form.Xfa;
        Assert.True(result.ReadComplete);
        var numbers = result.Data.Where(node => node.LocalName == "number").ToArray();
        Assert.Equal(3, numbers.Length);
        Assert.Equal(3, numbers.Select(node => node.NodePath).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("/XFA [(template) 7 0 R (datasets)]")]
    [InlineData("/XFA [(template) 7 0 R (datasets) 8 0 R (datasets) 8 0 R]")]
    [InlineData("/XFA [(template) 7 0 R (datasets) 99 0 R]")]
    [InlineData("/XFA [(template) 7 0 R (datasets) (not a stream)]")]
    public void MalformedPacketsCannotProduceCompleteData(string xfa)
    {
        var result = Inspect(xfa, Stream(Template), Stream(DataStart + DataEnd));
        Assert.Null(result.Basics.ParseError);
        Assert.False(result.Form.Xfa.ReadComplete);
    }

    [Theory]
    [InlineData("<!DOCTYPE datasets [<!ENTITY x SYSTEM 'file:///not-read'>]><datasets>&x;</datasets>")]
    [InlineData("<datasets/>")]
    [InlineData("<xfa:datasets xmlns:xfa='http://www.xfa.org/schema/xfa-data/1.0/'><xfa:data/><xfa:data/></xfa:datasets>")]
    [InlineData("<broken")]
    public void UnsafeOrWrongXmlIsIncompleteWithoutLosingBasicPdf(string xml)
    {
        var result = Inspect("/XFA [(template) 7 0 R (datasets) 8 0 R]", Stream(Template), Stream(xml));
        Assert.Null(result.Basics.ParseError);
        Assert.False(result.Form.Xfa.ReadComplete);
    }

    [Fact]
    public void XfaXmlDepthAndExpandedByteLimitsPropagate()
    {
        var xml = DataStart + "<a><b><c><d>value</d></c></b></a>" + DataEnd;
        var bytes = PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/AcroForm << /Fields [] /XFA [(template) 7 0 R (datasets) 8 0 R] >>", Stream(Template), Stream(xml));
        using var stream = new MemoryStream(bytes);
        Assert.Throws<PdfInspectionLimitException>(() => new PdfPigPackageInspector().Inspect(stream, "0000/form.pdf", new() { MaxXmlDepth = 3 }, default));
        stream.Position = 0;
        Assert.Throws<PdfInspectionLimitException>(() => new PdfPigPackageInspector().Inspect(stream, "0000/form.pdf", new() { MaxXmlCharacters = 30 }, default));
    }

    [Fact]
    public void SingleXdpStreamKeepsDataWithoutRunningTemplateScripts()
    {
        var xdp = "<xdp:xdp xmlns:xdp='http://ns.adobe.com/xdp/'>" + Template + DataStart + "<form><number>000123</number></form>" + DataEnd + "</xdp:xdp>";
        var result = Inspect("/XFA 7 0 R", Stream(xdp)).Form;
        Assert.True(result.Xfa.ReadComplete, string.Join(" ", result.Xfa.IncompleteReasons));
        Assert.False(result.Complete);
        Assert.Equal("000123", Assert.Single(result.Xfa.Data, node => node.LocalName == "number").DirectText);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(xdp))).ToLowerInvariant(), Assert.Single(result.Xfa.Packets).Sha256);
    }

    [Theory]
    [InlineData("/Filter 99 0 R")]
    [InlineData("/Filter [99 0 R]")]
    [InlineData("/DecodeParms 99 0 R")]
    [InlineData("/DecodeParms [99 0 R]")]
    [InlineData("/Filter /LZWDecode")]
    public void ReviewUnresolvedOrUnsupportedStreamMetadataCannotBecomeNoFilter(string metadata)
    {
        var data = Stream(DataStart + DataEnd).Replace("<< /Length", "<< " + metadata + " /Length", StringComparison.Ordinal);
        var result = Inspect("/XFA [(template) 7 0 R (datasets) 8 0 R]", Stream(Template), data);
        Assert.Null(result.Basics.ParseError);
        Assert.False(result.Form.Xfa.ReadComplete);
    }

    [Fact]
    public void ReviewEnvelopeFragmentsMustActuallyEncloseThePackets()
    {
        var result = Inspect("/XFA [(xdp:xdp) 7 0 R (template) 8 0 R (datasets) 9 0 R (</xdp:xdp>) 10 0 R]",
            Stream("broken header"), Stream(Template), Stream(DataStart + DataEnd), Stream("broken trailer"));
        Assert.Null(result.Basics.ParseError);
        Assert.False(result.Form.Xfa.ReadComplete);
    }

    [Fact]
    public void ReviewEnvelopeCannotHideTheInsertedBoundaryInsideAComment()
    {
        var header = "<xdp:xdp xmlns:xdp='http://ns.adobe.com/xdp/'><boundary xmlns='urn:ratools:xfa-envelope-check'/><!--";
        var result = Inspect("/XFA [(xdp:xdp) 7 0 R (template) 8 0 R (datasets) 9 0 R (</xdp:xdp>) 10 0 R]",
            Stream(header), Stream(Template), Stream(DataStart + DataEnd), Stream("--></xdp:xdp>"));
        Assert.False(result.Form.Xfa.ReadComplete);
    }

    [Fact]
    public void ReviewRepeatedResolutionFailureMustNotPublishUndecodedPacketFacts()
    {
        var template = Stream(Template).Replace("<< /Length", "<< /Filter 99 0 R /Length", StringComparison.Ordinal);
        var data = Stream(DataStart + "<form><number>123456</number></form>" + DataEnd)
            .Replace("<< /Length", "<< /Filter 99 0 R /Length", StringComparison.Ordinal);
        var result = Inspect("/XFA [(template) 7 0 R (datasets) 8 0 R]", template, data).Form.Xfa;
        Assert.False(result.ReadComplete);
        Assert.Empty(result.Packets);
        Assert.Empty(result.Data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReviewStoredDeflateBlockMustHaveFinalFlagEvenWithCorrectAdler(bool finalBlock)
    {
        var xml = "<xdp:xdp xmlns:xdp='http://ns.adobe.com/xdp/'>" + Template + DataStart + DataEnd + "</xdp:xdp>";
        var content = System.Text.Encoding.ASCII.GetBytes(xml);
        var encoded = new byte[2 + 5 + content.Length + 4];
        encoded[0] = 0x78; encoded[1] = 0x01;
        encoded[2] = finalBlock ? (byte)1 : (byte)0; // Stored block BFINAL; no hidden compression library assumptions.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(3), (ushort)content.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(5), (ushort)~(ushort)content.Length);
        content.CopyTo(encoded, 7);
        uint a = 1, b = 0;
        foreach (var value in content) { a = (a + value) % 65521; b = (b + a) % 65521; }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(encoded.Length - 4), b << 16 | a);
        using var pdf = new MemoryStream(CompressedPdf(encoded));
        var result = new PdfPigPackageInspector().Inspect(pdf, "0000/form.pdf", new(), default);
        Assert.Null(result.Basics.ParseError);
        Assert.Equal(finalBlock, result.Form.Xfa.ReadComplete);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("truncated")]
    [InlineData("short-trailer")]
    [InlineData("trailing")]
    [InlineData("corrupt")]
    [InlineData("ratio")]
    public void CompressedPacketsRequireCompleteBoundedZlib(string variant)
    {
        var xml = "<xdp:xdp xmlns:xdp='http://ns.adobe.com/xdp/'>" + Template + DataStart +
            "<form><number>" + new string('1', 2000) + "</number></form>" + DataEnd + "</xdp:xdp>";
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(System.Text.Encoding.UTF8.GetBytes(xml));
        var data = compressed.ToArray();
        if (variant == "truncated") data = data[..^4];
        if (variant == "short-trailer") data = data[..^1];
        if (variant == "trailing") data = data.Append((byte)0).ToArray();
        if (variant == "corrupt") data[^1] ^= 0xff;
        using var pdf = new MemoryStream(CompressedPdf(data));
        if (variant == "ratio")
            Assert.Throws<PdfInspectionLimitException>(() => new PdfPigPackageInspector().Inspect(pdf, "0000/form.pdf", new() { MaxCompressionRatio = 1 }, default));
        else
        {
            var result = new PdfPigPackageInspector().Inspect(pdf, "0000/form.pdf", new(), default);
            Assert.Null(result.Basics.ParseError);
            Assert.Equal(variant == "valid", result.Form.Xfa.ReadComplete);
        }
    }

    private static byte[] CompressedPdf(byte[] data)
    {
        var ascii = System.Text.Encoding.ASCII;
        var objects = new List<byte[]>
        {
            ascii.GetBytes("<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] /XFA 4 0 R >> >>"),
            ascii.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            ascii.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>"),
            ascii.GetBytes("<< /Filter /FlateDecode /Length " + data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>\nstream\n")
                .Concat(data).Concat(ascii.GetBytes("\nendstream")).ToArray()
        };
        using var result = new MemoryStream();
        var offsets = new List<long>();
        Write("%PDF-1.4\n");
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(result.Position);
            Write((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 obj\n");
            result.Write(objects[index]); Write("\nendobj\n");
        }
        var xref = result.Position;
        Write("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n" + xref.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n%%EOF\n");
        return result.ToArray();
        void Write(string value) => result.Write(ascii.GetBytes(value));
    }

    [Theory]
    [InlineData("2252", "db_aplcnt_nmbr", "921983dcb9ac14308617684c52e516f283cdf343adb7e0f3ab52fef09b1b1e7e")]
    [InlineData("2253", "db_nda_bla_nmbr", "1e503e6a81439c66e2a5c2e30a4e6e5ac59509dd9571095a267f2bfc8532c526")]
    [InlineData("3674", "db_nda_bla_nmbr", "1ebcaeb1db0475f9d91cdb601347e94119d6ec2060f6e5b24543ecaab6425044")]
    [InlineData("3938", "DMF_N", "527053cc5b6b579df6d058335b1b38a06506adf4b1995e1f04c4102673d8daab")]
    [InlineData("3988", "appnumb", "50b338c0ec53df5c646947401913c23e3f24c80682a838efc91ce4b8f4d5e798")]
    public void PinnedOfficialXfaFormsHaveReadablePacketsButNoQualifiedTemplate(string form, string fieldName, string sha256)
    {
        var root = Environment.GetEnvironmentVariable("RATOOLS_TEST_REPOSITORY_ROOT") ?? throw new InvalidOperationException("Repository root required.");
        using var stream = File.OpenRead(Path.Combine(root, "reference/publisher/official-sources/2026-11-20-forms/fda-form-" + form + ".pdf"));
        Assert.Equal(sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant());
        stream.Position = 0;
        var result = new PdfPigPackageInspector().Inspect(stream, "0000/form.pdf", new(), default);
        Assert.Null(result.Basics.ParseError);
        Assert.True(result.Form.HasXfa);
        Assert.False(result.Form.Complete);
        Assert.True(result.Form.Xfa.ReadComplete, string.Join(" ", result.Form.Xfa.IncompleteReasons));
        Assert.Contains(result.Form.Xfa.Data, node => node.LocalName == fieldName);
    }

    private static string Stream(string xml) => "<< /Length " + System.Text.Encoding.ASCII.GetByteCount(xml).ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>\nstream\n" + xml + "\nendstream";
    private static PackagePdfFacts Inspect(string xfa, params string[] objects)
    {
        using var stream = new MemoryStream(PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", "/AcroForm << /Fields [] " + xfa + " >>", objects));
        return new PdfPigPackageInspector().Inspect(stream, "0000/form.pdf", new(), default);
    }
}
