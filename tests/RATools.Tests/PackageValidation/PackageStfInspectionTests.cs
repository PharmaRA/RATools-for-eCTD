using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class PackageStfInspectionTests
{
    private const string Stf = """
        <?xml version="1.0" encoding="UTF-8"?>
        <?xml-stylesheet type="text/xsl" href="../../util/style/ich-stf-stylesheet-2-3.xsl"?>
        <!DOCTYPE ectd:study SYSTEM "../../util/dtd/ich-stf-v2-2.dtd">
        <ectd:study xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3.org/1999/xlink" dtd-version="2.2">
        <study-identifier><title>Study A</title><study-id>A</study-id><category name="study-type" info-type="ich">toxicity</category></study-identifier>
        <study-document><doc-content xlink:href="../../index.xml#report"><file-tag name="study-report-body" info-type="ich"/></doc-content></study-document>
        </ectd:study>
        """;

    [Theory]
    [InlineData("0001/m4/study/stf-a.xml")]
    [InlineData("0001/m4/study/not-named-stf.xml")]
    public async Task RealStfIsParsedSeparatelyFromLifecycleBackbones(string path)
    {
        var input = new Input(path, Stf);
        var result = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        Assert.True(result.StudyTaggingFiles.InventoryComplete);
        var stf = Assert.Single(result.StudyTaggingFiles.Documents);
        Assert.True(stf.ReadComplete);
        Assert.Equal(CheckStatus.Pass, stf.DtdStatus);
        Assert.Equal(path, stf.LogicalPath);
        Assert.Equal(input.Manifest.Files.Single(file => file.LogicalPath == path).Sha256, stf.CapturedSha256);
        Assert.Contains("stf-dtd", stf.ResolvedAssets);
        Assert.Equal("../../index.xml#report", Assert.Single(stf.Elements, item => item.LocalName == "doc-content").Attribute("href", "http://www.w3.org/1999/xlink"));
        Assert.DoesNotContain(result.Backbones, document => document.LogicalPath == path);
        Assert.Equal(2, result.Backbones.Count);
    }

    [Theory]
    [InlineData("stf-a.txt", true)]
    [InlineData("study.pdf", true)]
    [InlineData("study", true)]
    [InlineData("study.xml.bak", true)]
    [InlineData("study.txt", false)]
    [InlineData("study.XML", false)]
    public async Task Fda1867DiscoveryDoesNotAssumeTheExtensionBeingValidated(string name, bool referenced)
    {
        var path = "0001/m4/study/" + name;
        var input = new Input(path, Stf, referenced: referenced);
        var result = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        Assert.True(result.StudyTaggingFiles.InventoryComplete);
        var document = Assert.Single(result.StudyTaggingFiles.Documents);
        Assert.Equal(path, document.LogicalPath);
        Assert.True(document.ReadComplete);
        Assert.Equal(CheckStatus.Pass, document.DtdStatus);
    }

    [Theory]
    [InlineData("utf8", true)]
    [InlineData("utf16le", true)]
    [InlineData("utf16be", true)]
    [InlineData("utf32le", true)]
    [InlineData("utf32be", true)]
    [InlineData("utf16le", false)]
    [InlineData("utf16be", false)]
    [InlineData("utf32le", false)]
    [InlineData("utf32be", false)]
    public async Task Fda1867DiscoveryUsesContentAcrossXmlEncodings(string encodingName, bool bom)
    {
        Encoding encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(bom),
            "utf16le" => new UnicodeEncoding(false, bom),
            "utf16be" => new UnicodeEncoding(true, bom),
            "utf32le" => new UTF32Encoding(false, bom),
            _ => new UTF32Encoding(true, bom)
        };
        var xml = Stf[Stf.IndexOf("<!DOCTYPE", StringComparison.Ordinal)..];
        // A no-BOM wide encoding starts with '<' so the standard XML autodetector can identify it.
        if (bom || encodingName == "utf8") xml = " \r\n\t" + xml;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
        var input = new Input("0001/m4/study/study.bin", xml, content: bytes);
        var result = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        var document = Assert.Single(result.StudyTaggingFiles.Documents);
        Assert.True(document.ReadComplete);
        Assert.Equal(CheckStatus.Pass, document.DtdStatus);
        Assert.True(result.StudyTaggingFiles.InventoryComplete);
    }

    [Theory]
    [InlineData("report.pdf", "%PDF-1.4 ordinary")]
    [InlineData("stf-a.pdf", "%PDF-1.4 ordinary")]
    [InlineData("data.xpt", "HEADER RECORD*******LIBRARY HEADER RECORD!!!!!!!")]
    [InlineData("ordinary.txt", "ordinary text")]
    [InlineData("ordinary.bin", "")]
    [InlineData("metadata.txt", "<metadata/>")]
    public async Task Fda1867DiscoveryDoesNotPromoteOrdinaryContent(string name, string content)
    {
        var result = await new PackageXmlInspector().InspectAsync(new Input("0001/m4/study/" + name, content), PackageValidationCatalog.UsProfile);
        Assert.True(result.StudyTaggingFiles.InventoryComplete);
        Assert.Empty(result.StudyTaggingFiles.Documents);
    }

    [Theory]
    [InlineData("<ectd:study xmlns:ectd='http://www.ich.org/ectd'")]
    [InlineData("   ")]
    public async Task Fda1867XmlLikeOrUnresolvedContentCannotProveEmptyInventory(string content)
    {
        var input = new Input("0001/m4/study/study.bin", content);
        var result = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        Assert.False(result.StudyTaggingFiles.InventoryComplete);
    }

    [Fact]
    public async Task Fda1867NonXmlExtensionStillPropagatesReadCancellation()
    {
        var input = new Input("0001/m4/study/study.bin", Stf);
        using var cancel = new CancellationTokenSource();
        input.CancelOnStfRead = cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile, cancel.Token));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("utf32")]
    public async Task Fda1867ShortReadsDoNotSplitEncodingEvidence(string kind)
    {
        Encoding encoding = kind switch { "utf16" => new UnicodeEncoding(true, true), "utf32" => new UTF32Encoding(false, true), _ => new UTF8Encoding(true) };
        var xml = " \t" + Stf[Stf.IndexOf("<!DOCTYPE", StringComparison.Ordinal)..];
        var input = new Input("0001/m4/study/study.bin", xml, content: encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray()) { ShortReads = true };
        var result = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.StudyTaggingFiles.Documents).DtdStatus);
        Assert.True(result.StudyTaggingFiles.InventoryComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fda1867PrefixLimitsAndReadFailuresRetainUnknown(bool unreadable)
    {
        var input = new Input("0001/m4/study/study.bin", new string(' ', 4096) + Stf) { FailRead = unreadable };
        var result = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        Assert.False(result.StudyTaggingFiles.InventoryComplete);
        Assert.Empty(result.StudyTaggingFiles.Documents);
        Assert.Contains(result.StudyTaggingFiles.Findings, finding => finding.Code == "STF_PREFIX_UNRESOLVED");
    }

    [Fact]
    public async Task Fda1867PrefixReadsShareOneDiscoveryWorkBudget()
    {
        var input = new Input("0001/m4/study/study.bin", new string(' ', 4096));
        var baseline = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        var limited = new Input("0001/m4/study/study.bin", new string(' ', 4096), new() { MaxXmlNodes = 2 });
        var result = await PackageStfInspector.InspectAsync(limited, PackageXmlAssets.Current, baseline.Backbones, new(limited.Limits), default);
        Assert.False(result.InventoryComplete);
        Assert.Contains(result.Findings, finding => finding.Code == "STF_PREFIX_WORK_LIMIT");
    }

    [Theory]
    [InlineData("FFD8FFE000104A46494600", true)]
    [InlineData("EF010203", true)]
    [InlineData("20003C", false)]
    [InlineData("2000", false)]
    [InlineData("EFBB", false)]
    public async Task Fda1867BinarySignaturesAndTruncatedWidePrefixesStayDistinct(string hex, bool complete)
    {
        var input = new Input("0001/m4/study/content.bin", "bytes", content: Convert.FromHexString(hex));
        var result = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        Assert.Equal(complete, result.StudyTaggingFiles.InventoryComplete);
        Assert.Empty(result.StudyTaggingFiles.Documents);
    }

    [Fact]
    public async Task Fda1867PrefixBudgetIsNotResetForEachOrdinaryFile()
    {
        var baselineInput = new Input("0001/m4/study/a.bin", "%PDF-1.4");
        var baseline = await new PackageXmlInspector().InspectAsync(baselineInput, PackageValidationCatalog.UsProfile);
        // Each file requires two units (open/read); three units cannot cover both.
        var limited = new Input("0001/m4/study/a.bin", "%PDF-1.4", new() { MaxXmlNodes = 3 }, secondContent: "%PDF-1.4");
        var result = await PackageStfInspector.InspectAsync(limited, PackageXmlAssets.Current, baseline.Backbones, new(limited.Limits), default);
        Assert.False(result.InventoryComplete);
        Assert.Contains(result.Findings, finding => finding.Code == "STF_PREFIX_WORK_LIMIT");
    }

    [Theory]
    [InlineData("other-root")]
    [InlineData("truncated")]
    [InlineData("internal-entity")]
    [InlineData("remote-dtd")]
    [InlineData("wrong-version")]
    [InlineData("missing")]
    public async Task InvalidOrMissingStfCannotProveCompleteInventory(string variant)
    {
        var xml = variant switch
        {
            "other-root" => "<different/>",
            "truncated" => Stf[..^12],
            "internal-entity" => Stf.Replace("../../util/dtd/ich-stf-v2-2.dtd\">", "../../util/dtd/ich-stf-v2-2.dtd\" [<!ENTITY outside SYSTEM 'file:///not-read'>]>", StringComparison.Ordinal),
            "remote-dtd" => Stf.Replace("../../util/dtd/ich-stf-v2-2.dtd", "https://not-contacted.invalid/stf.dtd", StringComparison.Ordinal),
            "wrong-version" => Stf.Replace("dtd-version=\"2.2\"", "dtd-version=\"9.9\"", StringComparison.Ordinal),
            "missing" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        var result = await new PackageXmlInspector().InspectAsync(new Input("0001/m4/study/stf-a.xml", xml), PackageValidationCatalog.UsProfile);
        Assert.False(result.StudyTaggingFiles.InventoryComplete);
        Assert.NotEmpty(result.StudyTaggingFiles.Findings);
    }

    [Fact]
    public async Task FilenameAloneDoesNotHideOrdinaryXmlOrProveNoStf()
    {
        var result = await new PackageXmlInspector().InspectAsync(new Input("0001/m4/study/metadata.xml", "<metadata/>"), PackageValidationCatalog.UsProfile);
        Assert.True(result.StudyTaggingFiles.InventoryComplete);
        Assert.Empty(result.StudyTaggingFiles.Documents);
    }

    [Fact]
    public async Task DefaultAttributesRemainDistinctFromExplicitValues()
    {
        var xml = Stf.Replace(" dtd-version=\"2.2\"", "", StringComparison.Ordinal);
        var result = await new PackageXmlInspector().InspectAsync(new Input("0001/m4/study/stf-a.xml", xml), PackageValidationCatalog.UsProfile);
        var stf = Assert.Single(result.StudyTaggingFiles.Documents);
        Assert.True(stf.ReadComplete);
        var root = stf.Elements[0];
        Assert.True(root.Attributes["dtd-version"].IsDefault);
        Assert.Null(root.Attributes["dtd-version"].ExplicitValue);
        Assert.Equal("study-type", Assert.Single(stf.Elements, item => item.LocalName == "category").Attributes["name"].ExplicitValue);
    }

    [Fact]
    public async Task StfReadLimitCannotBecomeAnEmptyCompleteInventory()
    {
        var result = await new PackageXmlInspector().InspectAsync(new Input("0001/m4/study/stf-a.xml", Stf, new() { MaxBackbones = 2 }), PackageValidationCatalog.UsProfile);
        Assert.False(result.StudyTaggingFiles.InventoryComplete);
    }

    [Fact]
    public async Task UnreferencedStudyRootStillParticipatesInDiscovery()
    {
        var result = await new PackageXmlInspector().InspectAsync(new Input("0001/m4/study/other.xml", Stf, referenced: false), PackageValidationCatalog.UsProfile);
        Assert.True(result.StudyTaggingFiles.InventoryComplete);
        Assert.Single(result.StudyTaggingFiles.Documents);
    }

    [Fact]
    public async Task ReviewStfDeclarationWithWrongRootCannotDisappearAsOrdinaryXml()
    {
        const string xml = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><different/>";
        var result = await new PackageXmlInspector().InspectAsync(new Input("0001/m4/study/metadata.xml", xml), PackageValidationCatalog.UsProfile);
        Assert.False(result.StudyTaggingFiles.InventoryComplete);
        Assert.NotEmpty(result.StudyTaggingFiles.Findings);
    }

    [Fact]
    public async Task ReviewFixedDtdNamespaceDefaultsStillReachFormalReader()
    {
        var xml = Stf.Replace(" xmlns:ectd=\"http://www.ich.org/ectd\"", "", StringComparison.Ordinal)
            .Replace(" xmlns:xlink=\"http://www.w3.org/1999/xlink\"", "", StringComparison.Ordinal);
        var result = await new PackageXmlInspector().InspectAsync(new Input("0001/m4/study/metadata.xml", xml), PackageValidationCatalog.UsProfile);
        var document = Assert.Single(result.StudyTaggingFiles.Documents);
        Assert.True(document.ReadComplete);
        Assert.Equal(CheckStatus.Pass, document.DtdStatus);
        Assert.True(document.Elements[0].Attributes["xmlns:ectd"].IsDefault);
    }

    [Fact]
    public async Task ProbeAndFormalReaderSpendTheSameNodeBudget()
    {
        var input = new Input("0001/m4/study/metadata.xml", Stf);
        var baseline = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        var limits = new PackageReadLimits { MaxXmlNodes = 10 };
        var findings = new XmlInspectionFindings(limits);
        for (var index = 0; index < limits.MaxXmlNodes - 1; index++) findings.CountNode(1, new());
        var result = await PackageStfInspector.InspectAsync(input, PackageXmlAssets.Current, baseline.Backbones, findings, default);
        Assert.False(result.InventoryComplete);
        Assert.True(findings.StructureLimitReached);
        Assert.Contains(result.Findings, finding => finding.Code == "XML_STRUCTURE_LIMIT");
    }

    [Fact]
    public async Task CancellationDuringStfStreamReadPropagates()
    {
        var input = new Input("0001/m4/study/metadata.xml", Stf);
        using var cancel = new CancellationTokenSource();
        input.CancelOnStfRead = cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile, cancel.Token));
        Assert.True(cancel.IsCancellationRequested);
    }

    [Fact]
    public async Task FormalStfReadContinuesSpendingTheProbeBudget()
    {
        var xml = Stf[Stf.IndexOf("<!DOCTYPE", StringComparison.Ordinal)..];
        var input = new Input("0001/m4/study/metadata.xml", xml);
        var baseline = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        var before = input.StfOpenCount;
        var limits = new PackageReadLimits { MaxXmlNodes = 10 };
        var findings = new XmlInspectionFindings(limits);
        for (var index = 0; index < limits.MaxXmlNodes - 2; index++) findings.CountNode(1, new());
        var result = await PackageStfInspector.InspectAsync(input, PackageXmlAssets.Current, baseline.Backbones, findings, default);
        Assert.Equal(2, input.StfOpenCount - before); // Probe reaches DOCTYPE; formal reader opens and consumes the remainder.
        Assert.False(result.InventoryComplete);
        Assert.False(Assert.Single(result.Documents).ReadComplete);
        Assert.True(findings.StructureLimitReached);
        Assert.Contains(result.Findings, finding => finding.Code == "XML_STRUCTURE_LIMIT");
    }

    [Fact]
    public async Task CancellationPropagatesAndIchOnlyDoesNotAcquireUsStfScope()
    {
        var input = new Input("0001/m4/study/stf-a.xml", Stf);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile, cancel.Token));
        var result = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.IchProfile);
        Assert.False(result.StudyTaggingFiles.InventoryComplete);
        Assert.Empty(result.StudyTaggingFiles.Documents);
    }

    [Fact]
    public async Task ArchivedSnapshotWithoutStfAssetDoesNotBorrowCurrentAsset()
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        var originalAssets = snapshot.Resources.Single(resource => resource.Key == "xml-manifest");
        var manifest = JsonNode.Parse(originalAssets.ReadBytes())!;
        var assets = manifest["assets"]!.AsArray();
        foreach (var item in assets.Where(item => item!["id"]!.GetValue<string>().StartsWith("stf-", StringComparison.Ordinal)).ToArray()) assets.Remove(item);
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var rulesResource = snapshot.Resources.Single(resource => resource.Key == "rules");
        var rules = JsonNode.Parse(rulesResource.ReadBytes())!;
        rules["sources"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "ratools-package-xml-assets-v1")!["sha256"] =
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var resources = snapshot.Resources.Where(resource => resource.Key is not ("xml-manifest" or "rules" or "xml-stf-dtd" or "xml-stf-style"))
            .Append(StandardsSnapshotResource.Create("xml-manifest", originalAssets.Category, originalAssets.RelativePath, originalAssets.MediaType, bytes))
            .Append(StandardsSnapshotResource.Create("rules", rulesResource.Category, rulesResource.RelativePath, rulesResource.MediaType, Encoding.UTF8.GetBytes(rules.ToJsonString())));
        var historical = StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc);
        var context = PackageStandardsContext.FromSnapshot(historical, selectionDate: new(2026, 11, 20));
        var result = await new PackageXmlInspector(standards: context).InspectAsync(new Input("0001/m4/study/stf-a.xml", Stf), PackageValidationCatalog.UsProfile);
        Assert.False(result.StudyTaggingFiles.InventoryComplete);
        Assert.Empty(result.StudyTaggingFiles.Documents);
        Assert.Contains(result.StudyTaggingFiles.Findings, finding => finding.Code == "STF_ASSET_UNAVAILABLE");
    }

    private sealed class Input : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string _stfPath;
        public int StfOpenCount { get; private set; }
        public CancellationTokenSource? CancelOnStfRead { get; set; }
        public bool ShortReads { get; set; }
        public bool FailRead { get; set; }
        public Input(string stfPath, string? xml, PackageReadLimits? limits = null, bool referenced = true, byte[]? content = null, string? secondContent = null)
        {
            _stfPath = stfPath;
            var id = Guid.NewGuid();
            var baseline = new PackageRegionalInspectionTests.MemoryInput(id, "0001", Fda47AttributeRulesTests.Valid(), PackageValidationCatalog.UsProfile);
            _files = baseline.Manifest.Files.ToDictionary(file => file.LogicalPath, file => Read(baseline.OpenRead(file.LogicalPath)), StringComparer.Ordinal);
            var index = Encoding.UTF8.GetString(_files["0001/index.xml"]);
            var closing = index.LastIndexOf("</", StringComparison.Ordinal);
            var relative = stfPath[5..];
            if (referenced) index = index.Insert(closing, "<m4-nonclinical-study-reports><m4-3-literature-references><leaf ID=\"stf\" operation=\"new\" xlink:href=\"" + relative + "\"><title>Study</title></leaf></m4-3-literature-references></m4-nonclinical-study-reports>");
            _files["0001/index.xml"] = Encoding.UTF8.GetBytes(index);
            if (xml is not null) _files[stfPath] = content ?? Encoding.UTF8.GetBytes(xml);
            if (secondContent is not null) _files["0001/m4/study/b.bin"] = Encoding.UTF8.GetBytes(secondContent);
            Limits = limits ?? new();
            Manifest = new(id, "0001", _files.Select(pair => new PackageInputFile(pair.Key, pair.Value.Length,
                Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(pair.Value)).ToLowerInvariant())), [], [], []);
        }
        private static byte[] Read(Stream stream) { using (stream) { using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); } }
        public string SourceId => "memory-stf";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; }
        public Stream OpenRead(string logicalPath)
        {
            if (!_files.TryGetValue(logicalPath, out var bytes)) throw new FileNotFoundException(logicalPath);
            if (logicalPath == _stfPath) StfOpenCount++;
            if (logicalPath == _stfPath && FailRead) throw new IOException("Synthetic unreadable content.");
            if (logicalPath == _stfPath && ShortReads) return new ShortStream(bytes);
            return logicalPath == _stfPath && CancelOnStfRead is { } cancel ? new CancellingStream(bytes, cancel) : new MemoryStream(bytes, writable: false);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancel) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancel.Cancel(); return base.ReadAsync(buffer, cancellationToken); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { cancel.Cancel(); return base.ReadAsync(buffer, offset, count, cancellationToken); }
    }

    private sealed class ShortStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => base.ReadAsync(buffer, offset, Math.Min(1, count), cancellationToken);
    }
}
