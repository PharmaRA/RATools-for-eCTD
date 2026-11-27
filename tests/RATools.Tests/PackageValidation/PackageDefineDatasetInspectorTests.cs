using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageDefineDatasetInspectorTests
{
    [Theory]
    [InlineData("dm.xpt", "Read", true)]
    [InlineData("data.bin", "Read", true)]
    [InlineData("missing.xpt", "Missing", false)]
    [InlineData("https://example.invalid/dm.xpt", "UnsupportedPath", false)]
    [InlineData("bad.xpt", "Read", true)]
    public async Task DeclaredFileIsAssociatedWithActualCapturedContent(string href, string expected, bool opensDataset)
    {
        await using var capture = new Capture(href);
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        Assert.Equal(DefineDatasetInspectionStatus.Read, result.Status);
        Assert.True(result.TraversalComplete);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(expected, dataset.Status.ToString());
        Assert.Equal(opensDataset ? 2 : 1, capture.Opens.Count);
        if (opensDataset)
        {
            Assert.Equal(capture.SourceId, dataset.Xpt!.SourceId);
            Assert.Equal(capture.InputDigest, dataset.Xpt.InputDigest);
            Assert.Equal(href == "bad.xpt" ? XptReadStatus.Unsupported : XptReadStatus.Read, dataset.Xpt.Content!.Status);
        }
        else Assert.Null(dataset.Xpt);
    }

    [Theory]
    [InlineData("../../../0000/dm.xpt")]
    [InlineData("../../../0002/dm.xpt")]
    [InlineData("dm.xpt?query")]
    [InlineData("dm.xpt#fragment")]
    [InlineData("%64m.xpt")]
    [InlineData("file:///dm.xpt")]
    [InlineData("C:\\dm.xpt")]
    [InlineData("/dm.xpt")]
    [InlineData("../研究.xpt")]
    public async Task UnsupportedReferencesNeverOpenTheirTarget(string href)
    {
        await using var capture = new Capture(href);
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        Assert.Equal(DefineDatasetInspectionStatus.Read, result.Status);
        Assert.Equal(DefineDatasetContentStatus.UnsupportedPath, Assert.Single(result.Datasets).Status);
        Assert.Equal([DefinePath], capture.Opens);
    }

    [Theory]
    [InlineData("missing-reference")]
    [InlineData("ambiguous-reference")]
    [InlineData("missing-href")]
    public async Task UnavailableLeafDeclarationsDoNotBorrowAFileByName(string scenario)
    {
        await using var capture = new Capture(scenario: scenario);
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        Assert.Equal(DefineDatasetContentStatus.ReferenceUnavailable, Assert.Single(result.Datasets).Status);
        Assert.Single(capture.Opens);
    }

    [Theory]
    [InlineData("invalid-define")]
    [InlineData("unsupported-define")]
    [InlineData("missing-define")]
    public async Task UnavailableDefineDoesNotCreateAnEmptyCompletedAssociation(string scenario)
    {
        await using var capture = new Capture(scenario: scenario);
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        Assert.Equal(DefineDatasetInspectionStatus.DefineUnavailable, result.Status);
        Assert.NotNull(result.Define);
        Assert.False(result.TraversalComplete);
        Assert.Empty(result.Datasets);
        Assert.DoesNotContain("0001/m5/data/dm.xpt", capture.Opens);
    }

    [Fact]
    public async Task SameFileIsReadOnceButEveryDeclarationIsRetained()
    {
        await using var capture = new Capture(scenario: "duplicate-file");
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath,
            xptLimits: new(MaxBytes: capture.DatasetLength, MaxRows: 1, MaxCells: 1));
        Assert.True(result.TraversalComplete);
        Assert.Equal(2, result.Datasets.Count);
        Assert.Equal(2, capture.Opens.Count);
        Assert.Same(result.Datasets[0].Xpt, result.Datasets[1].Xpt);
        Assert.NotEqual(result.Datasets[0].Declaration.Oid, result.Datasets[1].Declaration.Oid);
    }

    [Theory]
    [InlineData("bytes", false)]
    [InlineData("bytes", true)]
    [InlineData("rows", false)]
    [InlineData("rows", true)]
    [InlineData("cells", false)]
    [InlineData("cells", true)]
    public async Task DistinctFilesShareBudgetsWithoutErasingEarlierContent(string budget, bool enough)
    {
        await using var capture = new Capture(scenario: "two-files");
        var limits = budget switch
        {
            "bytes" => new XptReadLimits(MaxBytes: capture.DatasetLength * 2 - (enough ? 0 : 1)),
            "rows" => new XptReadLimits(MaxRows: enough ? 2 : 1),
            _ => new XptReadLimits(MaxCells: enough ? 2 : 1)
        };
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath, xptLimits: limits);
        Assert.True(result.TraversalComplete);
        Assert.Equal(2, result.Datasets.Count);
        Assert.True(result.Datasets[0].Xpt!.Content!.StructureComplete);
        if (enough) Assert.True(result.Datasets[1].Xpt!.Content!.StructureComplete);
        else if (budget == "bytes")
        {
            Assert.Equal(DefineDatasetContentStatus.LimitExceeded, result.Datasets[1].Status);
            Assert.Null(result.Datasets[1].Xpt);
            Assert.Equal(2, capture.Opens.Count);
        }
        else Assert.Equal(XptReadStatus.LimitExceeded, result.Datasets[1].Xpt!.Content!.Status);
    }

    [Theory]
    [InlineData("dataset-io")]
    [InlineData("dataset-denied")]
    public async Task OneUnreadableTargetDoesNotEraseAnotherStableTarget(string scenario)
    {
        await using var capture = new Capture(scenario: scenario);
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        Assert.True(result.TraversalComplete);
        Assert.Equal(DefineDatasetContentStatus.Unavailable, result.Datasets[0].Status);
        Assert.True(result.Datasets[1].Xpt!.Content!.StructureComplete);
    }

    [Theory]
    [InlineData("hash", "InputChanged")]
    [InlineData("final-change", "InputChanged")]
    [InlineData("final-io", "Unavailable")]
    [InlineData("identity-source", "InputChanged")]
    [InlineData("identity-digest", "InputChanged")]
    [InlineData("identity-manifest", "InputChanged")]
    [InlineData("identity-limits", "InputChanged")]
    public async Task GroupIntegrityFailureClearsAllPreviouslyReadFacts(string scenario, string expected)
    {
        await using var capture = new Capture(scenario: scenario);
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        Assert.Equal(expected, result.Status.ToString());
        Assert.False(result.TraversalComplete);
        Assert.Null(result.Define);
        Assert.Empty(result.Datasets);
    }

    [Fact]
    public async Task InventoryWorkExhaustionCannotPublishACompletedGroup()
    {
        await using var capture = new Capture(scenario: "work-limit");
        var result = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        Assert.Equal(DefineDatasetInspectionStatus.LimitExceeded, result.Status);
        Assert.False(result.TraversalComplete);
        Assert.Empty(result.Datasets);
        Assert.Single(capture.Opens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalCancellationIsNeverConvertedToAnUnknownResult(bool during)
    {
        using var cancellation = new CancellationTokenSource();
        await using var capture = new Capture(scenario: during ? "cancel-dataset" : "valid", cancel: cancellation.Cancel);
        if (!during) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageDefineDatasetInspector.InspectAsync(capture,
            DefinePath, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ResultIdentityBindsLimitsEncodingAndPath()
    {
        await using var capture = new Capture();
        var first = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        var repeat = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath);
        var limited = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath, xptLimits: new(MaxRows: 2));
        var utf8 = await PackageDefineDatasetInspector.InspectAsync(capture, DefinePath, encoding: XptTextEncoding.Utf8);
        var absent = await PackageDefineDatasetInspector.InspectAsync(capture, "0001/absent.xml");
        Assert.Equal(first.SelectionDigest, repeat.SelectionDigest);
        Assert.NotEqual(first.SelectionDigest, limited.SelectionDigest);
        Assert.NotEqual(first.SelectionDigest, utf8.SelectionDigest);
        Assert.NotEqual(first.SelectionDigest, absent.SelectionDigest);
        Assert.Throws<NotSupportedException>(() => ((IList<DefineDatasetContent>)first.Datasets).Clear());
    }

    [Fact]
    public async Task InvalidLimitsAreRejectedBeforeReading()
    {
        await using var capture = new Capture();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PackageDefineDatasetInspector.InspectAsync(capture, DefinePath, xptLimits: new(MaxRows: -1)));
        Assert.Empty(capture.Opens);
    }

    private const string DefinePath = "0001/m5/data/define.xml";
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string _scenario;
        private readonly PackageInputManifest _manifest;
        private readonly Action? _cancel;
        private int _verifications;
        public Capture(string href = "dm.xpt", string scenario = "valid", Action? cancel = null)
        {
            _scenario = scenario; _cancel = cancel;
            var xpt = PackageXptReaderTests.Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
            DatasetLength = xpt.Length;
            XNamespace odm = "http://www.cdisc.org/ns/odm/v1.3", def = "http://www.cdisc.org/ns/def/v2.1", xlink = "http://www.w3.org/1999/xlink";
            var xml = XDocument.Parse(PackageDefineXmlReaderTests.Sample().Replace("dm.xpt", href, StringComparison.Ordinal));
            var group = xml.Descendants(odm + "ItemGroupDef").Single();
            if (scenario == "missing-reference") group.Attribute(def + "ArchiveLocationID")!.Remove();
            if (scenario == "missing-href") group.Element(def + "leaf")!.Attribute(xlink + "href")!.Remove();
            if (scenario == "ambiguous-reference") group.Add(new XElement(group.Element(def + "leaf")!));
            if (scenario is "duplicate-file" or "two-files" or "dataset-io" or "dataset-denied")
            {
                var second = new XElement(group); second.SetAttributeValue("OID", "IG.SECOND");
                if (scenario != "duplicate-file")
                {
                    second.SetAttributeValue(def + "ArchiveLocationID", "LF.SECOND");
                    second.Element(def + "leaf")!.SetAttributeValue("ID", "LF.SECOND");
                    second.Element(def + "leaf")!.SetAttributeValue(xlink + "href", "data.bin");
                }
                else second.Element(def + "leaf")!.Remove();
                group.AddAfterSelf(second);
            }
            if (scenario == "unsupported-define") xml.Descendants(odm + "MetaDataVersion").Single().SetAttributeValue(def + "DefineVersion", "2.0.0");
            _files = new(StringComparer.Ordinal)
            {
                [DefinePath] = Encoding.UTF8.GetBytes(xml.ToString() + (scenario == "invalid-define" ? "<broken" : "")),
                ["0001/m5/data/dm.xpt"] = xpt,
                ["0001/m5/data/data.bin"] = xpt,
                ["0001/m5/data/bad.xpt"] = Encoding.ASCII.GetBytes(new string('x', 80))
            };
            if (scenario == "work-limit") for (var index = 0; index < 100; index++) _files.Add("0001/extra" + index + ".bin", []);
            _manifest = new(Guid.NewGuid(), "0001", _files.Where(file => scenario != "missing-define" || file.Key != DefinePath).Select(file => new PackageInputFile(file.Key, file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public List<string> Opens { get; } = [];
        public int DatasetLength { get; }
        private bool Changed(string field) => _scenario == "identity-" + field && _verifications >= 6;
        public string SourceId => Changed("source") ? "changed" : "define-dataset-fixture";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest => Changed("manifest") ? new(_manifest.ApplicationId, "0001", [], [], [], []) : _manifest;
        public string? ArchiveSha256 => null;
        public string InputDigest => Changed("digest") ? new string('a', 64) : _manifest.Digest;
        public PackageReadLimits Limits => new() { MaxXmlNodes = Changed("limits") ? 400000 : _scenario == "work-limit" ? 80 : 500000 };
        public Stream OpenRead(string path)
        {
            Opens.Add(path);
            if (path.EndsWith("/dm.xpt", StringComparison.Ordinal))
            {
                if (_scenario == "cancel-dataset") _cancel!();
                if (_scenario == "dataset-io") throw new IOException("Unreadable dataset.");
                if (_scenario == "dataset-denied") throw new UnauthorizedAccessException("Dataset access denied.");
                if (_scenario == "hash") return new MemoryStream([.. _files[path][..^1], 0], false);
            }
            return new MemoryStream(_files[path], false);
        }
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); _verifications++;
            if (_verifications == 6 && _scenario == "final-change") throw new PackageInputChangedException(SourceId);
            if (_verifications == 6 && _scenario == "final-io") throw new IOException("Final verification unavailable.");
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
