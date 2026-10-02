using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageXmlInspectorTests
{
    private static readonly string[] ExpectedEuAssets = ["eu-dtd", "eu-envelope", "eu-leaf"];
    [Theory]
    [InlineData("0000")]
    [InlineData("0001")]
    [InlineData("0002")]
    [InlineData("0003")]
    public async Task HandAuthoredSequencesAreInspectedWithoutAnyPublishingModel(string sequence)
    {
        using var fixture = new XmlFixture(sequence);
        var inspection = await fixture.InspectAsync();
        Assert.NotEmpty(inspection.Backbones);
        Assert.All(inspection.Backbones, backbone => Assert.True(backbone.ReadComplete));
        Assert.All(inspection.Backbones, backbone => Assert.Equal(CheckStatus.Pass, backbone.DtdStatus));
        Assert.DoesNotContain(Findings(inspection), finding => finding.CheckStatus == CheckStatus.Fail);
        var ich = Assert.Single(inspection.Backbones, document => document.Kind == BackboneKind.Ich);
        Assert.Contains("ich-dtd", ich.ResolvedAssets);
        Assert.All(ich.Leaves, leaf => Assert.NotNull(leaf.Address));
        if (sequence == "0003")
        {
            var deleted = Assert.Single(ich.Leaves, leaf => leaf.Id == "beta-delete");
            Assert.Equal("delete", deleted.Operation);
            Assert.Null(deleted.Href);
            Assert.Equal("../0000/index.xml#beta-spec-v1", deleted.ModifiedFile);
        }
    }

    [Fact]
    public async Task IndependentBusinessOracleMatchesAttributedNodesAndDocumentScopedIds()
    {
        using var fixture = new XmlFixture();
        var inspection = await fixture.InspectAsync();
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(PackageRuleCatalogTests.Root, "tests/RATools.Tests/Fixtures/Publisher/expected.json")));
        foreach (var group in expected.RootElement.GetProperty("nodeGroups").EnumerateArray())
        {
            foreach (var rawAddress in group.GetProperty("leaves").EnumerateArray().Select(value => value.GetString()!).Where(value => value.StartsWith("0000/", StringComparison.Ordinal)))
            {
                var parts = rawAddress.Split('#');
                var backbone = Assert.Single(inspection.Backbones, document => document.LogicalPath == parts[0]);
                var leaf = Assert.Single(backbone.Leaves, leaf => leaf.Id == parts[1]);
                var node = backbone.Nodes.SingleOrDefault(node => node.NodePath == leaf.ParentNodePath);
                var ancestors = new List<ParsedCtdNode>();
                while (node is not null)
                {
                    ancestors.Add(node);
                    node = backbone.Nodes.SingleOrDefault(candidate => candidate.NodePath == node.ParentNodePath);
                }
                foreach (var ancestor in group.GetProperty("ancestors").EnumerateArray())
                {
                    var actual = Assert.Single(ancestors, candidate => candidate.DefinitionKey == ancestor.GetProperty("element").GetString());
                    foreach (var attribute in ancestor.GetProperty("attributes").EnumerateObject())
                        Assert.Equal(attribute.Value.GetString(), attribute.Name == "ID" ? actual.XmlId : actual.IdentityAttributes[attribute.Name]);
                    if (ancestor.TryGetProperty("title", out var title)) Assert.Equal(title.GetString(), actual.Title);
                    Assert.True(actual.IdentityComplete);
                }
            }
        }
        Assert.Equal(2, inspection.Backbones.Count(backbone => backbone.IdIndex.ContainsKey("overview")));
        Assert.All(inspection.Backbones, backbone => Assert.Single(backbone.IdIndex["overview"]));
        var ich = inspection.Backbones[0];
        Assert.NotEqual(Assert.Single(ich.Leaves, leaf => leaf.Id == "alpha-spec-v1").ContextKey,
            Assert.Single(ich.Leaves, leaf => leaf.Id == "beta-spec-v1").ContextKey);
        Assert.Equal(CheckStatus.Pass, Assert.Single(inspection.Checks, check => check.RuleId == "XML-IDS").Status);
    }

    [Fact]
    public async Task MissingMandatoryAttributeHasExactNodeAndSourceLocation()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "<m3-2-s-drug-substance substance=\"Drug A\" manufacturer=\"Alpha\">", "<m3-2-s-drug-substance substance=\"Drug A\">");
        var inspection = await fixture.InspectAsync();
        var finding = Assert.Single(Findings(inspection), finding => finding.Code == "MISSING_REQUIRED_NODE_ATTRIBUTE");
        Assert.Equal("@manufacturer", finding.Location.FieldPath);
        Assert.Equal("0000/index.xml", finding.Location.LogicalPath);
        Assert.Contains("m3-2-s-drug-substance[1]", finding.Location.NodePath, StringComparison.Ordinal);
        Assert.True(finding.Location.Line > 1);
        Assert.True(finding.Location.Column > 0);
        Assert.False(Assert.Single(inspection.Backbones[0].Leaves, leaf => leaf.Id == "alpha-spec-v1").ContextComplete);
    }

    [Fact]
    public async Task DuplicateIdsRemainAmbiguousAndBothOccurrencesAreLocated()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "ID=\"qos-beta\"", "ID=\"qos-alpha\"");
        var inspection = await fixture.InspectAsync();
        Assert.Equal(2, inspection.Backbones[0].IdIndex["qos-alpha"].Count);
        var duplicates = Findings(inspection).Where(finding => finding.Code == "DUPLICATE_XML_ID").ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.NotEqual(duplicates[0].Location.NodePath, duplicates[1].Location.NodePath);
        Assert.All(duplicates, finding => Assert.Equal("@ID", finding.Location.FieldPath));
    }

    [Theory]
    [InlineData("dtd-version=\"3.2\"", "dtd-version=\"3.1\"")]
    [InlineData("http://www.ich.org/ectd", "http://example.invalid/other")]
    [InlineData("<!DOCTYPE ectd:ectd SYSTEM \"util/dtd/ich-ectd-3-2.dtd\">", "")]
    public async Task MissingOrConflictingProfileDeclarationsDoNotProduceReusableIdentities(string old, string replacement)
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", old, replacement);
        var inspection = await fixture.InspectAsync();
        Assert.Equal(CheckStatus.Fail, Assert.Single(inspection.Checks, check => check.RuleId == "PROFILE-VERSION").Status);
        Assert.False(Assert.Single(inspection.Backbones).ReadComplete);
        Assert.Empty(inspection.Backbones[0].Leaves);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(inspection.Checks, check => check.RuleId == "XML-IDS").Status);
    }

    [Theory]
    [InlineData("https://untrusted.example.invalid/ich-ectd-3-2.dtd")]
    [InlineData("https://package.invalid/0000/util/dtd/ich-ectd-3-2.dtd")]
    [InlineData("file:///tmp/ich-ectd-3-2.dtd")]
    [InlineData("other/ich-ectd-3-2.dtd")]
    [InlineData("util/dtd/ich-ectd-3-2.dtd?ignored=true")]
    public async Task UnknownOrAbsoluteDtdIdentifiersNeverUseBasenameFallback(string systemId)
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "SYSTEM \"util/dtd/ich-ectd-3-2.dtd\"", "SYSTEM \"" + systemId + "\"");
        var inspection = await fixture.InspectAsync();
        Assert.Contains(Findings(inspection), finding => finding.Code == "UNTRUSTED_XML_RESOURCE");
        Assert.False(Assert.Single(inspection.Backbones).ReadComplete);
        Assert.Empty(inspection.Backbones[0].ResolvedAssets);
    }

    [Theory]
    [InlineData("<!ENTITY leak SYSTEM 'file:///outside.txt'>")]
    [InlineData("<!ENTITY % external SYSTEM 'https://untrusted.example.invalid/evil.dtd'>%external;")]
    [InlineData("<!ATTLIST m3-2-s-drug-substance manufacturer CDATA 'Forged'>")]
    public async Task InternalDeclarationsCannotOverridePinnedDtdOrLoadEntities(string subset)
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "SYSTEM \"util/dtd/ich-ectd-3-2.dtd\">", "SYSTEM \"util/dtd/ich-ectd-3-2.dtd\" [" + subset + "]>");
        var inspection = await fixture.InspectAsync();
        Assert.Contains(Findings(inspection), finding => finding.Code == "UNTRUSTED_XML_RESOURCE");
        Assert.Empty(Assert.Single(inspection.Backbones).Leaves);
    }

    [Theory]
    [InlineData("deep name.xml", "deep%20name.xml")]
    [InlineData("deep#name.xml", "deep%23name.xml")]
    public async Task CustomEncodedRegionalAddressUsesExactRelativeDtdResolution(string fileName, string hrefName)
    {
        using var fixture = new XmlFixture();
        var original = Path.Combine(fixture.Source, "m1/us/us-regional.xml");
        var destination = Path.Combine(fixture.Source, "m1/us/custom/" + fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(original, destination);
        fixture.Replace("m1/us/custom/" + fileName, "../../util/dtd/us-regional-v3-3.dtd", "../../../util/dtd/us-regional-v3-3.dtd");
        fixture.Replace("index.xml", "m1/us/us-regional.xml", "m1/us/custom/" + hrefName);
        var inspection = await fixture.InspectAsync();
        Assert.DoesNotContain(Findings(inspection), finding => finding.CheckStatus == CheckStatus.Fail);
        var regional = Assert.Single(inspection.Backbones, document => document.Kind == BackboneKind.UsRegional);
        Assert.Equal("0000/m1/us/custom/" + fileName, regional.LogicalPath);
        Assert.Equal("m1/us/custom/" + fileName, Assert.Single(regional.Leaves).Address!.BackboneRelativePath);
    }

    [Fact]
    public async Task OfficialEuModularDtdValidatesHandAuthoredRegionalXml()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "m1/us/us-regional.xml", "m1/eu/eu-regional.xml");
        var destination = Path.Combine(fixture.Source, "m1/eu/eu-regional.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures/PackageValidation/eu-regional.xml"), destination);
        var inspection = await fixture.InspectAsync(profile: PackageValidationCatalog.EuProfile);
        Assert.DoesNotContain(Findings(inspection), finding => finding.CheckStatus == CheckStatus.Fail);
        var eu = Assert.Single(inspection.Backbones, document => document.Kind == BackboneKind.EuRegional);
        Assert.Equal(ExpectedEuAssets, eu.ResolvedAssets);
        Assert.Equal("eu-cover", Assert.Single(eu.Leaves).Id);
        Assert.Equal(CheckStatus.Pass, Assert.Single(inspection.Checks, check => check.RuleId == "EU-REGIONAL-DTD").Status);
    }

    [Fact]
    public async Task IdenticalSiblingContextsArePreservedAndMarkedAmbiguous()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "manufacturer=\"Beta\"", "manufacturer=\"Alpha\"");
        var inspection = await fixture.InspectAsync();
        var substances = inspection.Backbones[0].Nodes.Where(node => node.DefinitionKey == "m3-2-s-drug-substance").ToArray();
        Assert.Equal(2, substances.Length);
        Assert.All(substances, node => Assert.True(node.IdentityAmbiguous));
        Assert.All(inspection.Backbones[0].Leaves.Where(leaf => leaf.Id is "alpha-spec-v1" or "beta-spec-v1"), leaf => Assert.False(leaf.ContextComplete));
        Assert.Contains(Findings(inspection), finding => finding.Code == "AMBIGUOUS_NODE_IDENTITY");
    }

    [Fact]
    public async Task ExtensionAtForbiddenParentIsLocatedAndCannotFormCompleteContext()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "<m3-quality>", "<m3-quality><node-extension ID=\"forbidden\"><title>Forbidden</title><leaf ID=\"forbidden-leaf\" operation=\"new\" checksum=\"0\" checksum-type=\"md5\"><title>Test</title></leaf></node-extension>");
        var inspection = await fixture.InspectAsync();
        Assert.Contains(Findings(inspection), finding => finding.RuleId == "NODE-EXTENSIONS" && finding.Code == "INVALID_NODE_PARENT");
        Assert.False(Assert.Single(inspection.Backbones[0].Leaves, leaf => leaf.Id == "forbidden-leaf").ContextComplete);
    }

    [Fact]
    public async Task OptionalExtensionIdIsNotInventedFromTitle()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "<node-extension ID=\"study-nc-001\">", "<node-extension>");
        var inspection = await fixture.InspectAsync();
        Assert.DoesNotContain(Findings(inspection), finding => finding.RuleId == "ICH-DTD" && finding.CheckStatus == CheckStatus.Fail);
        Assert.Contains(Findings(inspection), finding => finding.Code == "EXTENSION_IDENTITY_UNRESOLVED" && finding.CheckStatus == CheckStatus.NotEvaluated);
        Assert.False(Assert.Single(inspection.Backbones[0].Leaves, leaf => leaf.Id == "nc-report").ContextComplete);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(inspection.Checks, check => check.RuleId == "NODE-EXTENSIONS").Status);
    }

    [Theory]
    [InlineData("depth")]
    [InlineData("nodes")]
    [InlineData("characters")]
    [InlineData("entities")]
    [InlineData("backbones")]
    public async Task XmlResourceLimitsRemainVisibleAndBlockCompletedCoverage(string dimension)
    {
        using var fixture = new XmlFixture();
        var limits = dimension switch
        {
            "depth" => new PackageReadLimits { MaxXmlDepth = 2 },
            "nodes" => new PackageReadLimits { MaxXmlNodes = 2 },
            "characters" => new PackageReadLimits { MaxXmlCharacters = 100 },
            "entities" => new PackageReadLimits { MaxXmlEntityCharacters = 1 },
            "backbones" => new PackageReadLimits { MaxBackbones = 1 },
            _ => throw new ArgumentException("Unknown limit")
        };
        var inspection = await fixture.InspectAsync(limits);
        Assert.Equal(CheckStatus.Fail, Assert.Single(inspection.Checks, check => check.RuleId == "XML-LIMITS").Status);
        Assert.Contains(inspection.Checks, check => check.Status == CheckStatus.NotEvaluated);
        Assert.NotEqual(new PackageReadLimits().Digest(), inspection.LimitsDigest);
    }

    [Fact]
    public async Task FindingLimitStopsInspectionWithoutDroppingTheBlockingSignal()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "manufacturer=\"Alpha\"", "invalid=\"Alpha\"");
        var inspection = await fixture.InspectAsync(new() { MaxXmlFindings = 2 });
        Assert.Equal(2, Findings(inspection).Count());
        Assert.Contains(Findings(inspection), finding => finding.Code == "XML_FINDING_LIMIT");
        Assert.Empty(inspection.Backbones.SelectMany(document => document.Leaves));
    }

    [Fact]
    public async Task MalformedXmlNeverExposesPartialLeafIndexes()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "</ectd:ectd>", "");
        var inspection = await fixture.InspectAsync();
        var document = Assert.Single(inspection.Backbones);
        Assert.False(document.ReadComplete);
        Assert.Empty(document.Leaves);
        Assert.Empty(document.IdIndex);
        Assert.Contains(Findings(inspection), finding => finding.Code == "XML_NOT_WELL_FORMED");
    }

    [Fact]
    public async Task KnownDtdDefaultsAreRecordedWithoutPretendingTheyWereExplicitAttributes()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", " dtd-version=\"3.2\"", "");
        var inspection = await fixture.InspectAsync();
        Assert.True(inspection.Backbones[0].ReadComplete);
        Assert.True(inspection.Backbones[0].Elements[0].Attributes["dtd-version"].IsDefault);
        Assert.Equal(CheckStatus.Pass, Assert.Single(inspection.Checks, check => check.RuleId == "PROFILE-VERSION").Status);
    }

    [Fact]
    public async Task AZipUsesTheSameXmlIdentityResultsAsItsDirectory()
    {
        using var fixture = new XmlFixture();
        var directory = await fixture.InspectAsync();
        await using var zip = await fixture.CaptureZipAsync();
        var archive = await new PackageXmlInspector().InspectAsync(zip, PackageValidationCatalog.UsProfile);
        Assert.NotEqual(directory.InputDigest, archive.InputDigest);
        Assert.Equal(directory.Backbones.SelectMany(document => document.Leaves), archive.Backbones.SelectMany(document => document.Leaves));
        Assert.Equal(directory.Checks.Select(check => check.Status), archive.Checks.Select(check => check.Status));
    }

    [Fact]
    public async Task Utf16SourceAndExactUnicodeAttributeValuesArePreserved()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "encoding=\"utf-8\"", "encoding=\"utf-16\"");
        fixture.Replace("index.xml", "manufacturer=\"Alpha\"", "manufacturer=\" 阿尔法 \"");
        var index = Path.Combine(fixture.Source, "index.xml");
        File.WriteAllText(index, File.ReadAllText(index), Encoding.Unicode);
        var inspection = await fixture.InspectAsync();
        Assert.DoesNotContain(Findings(inspection), finding => finding.CheckStatus == CheckStatus.Fail);
        var node = Assert.Single(inspection.Backbones[0].Nodes, node => node.DefinitionKey == "m3-2-s-drug-substance" && node.IdentityAttributes["manufacturer"] == " 阿尔法 ");
        Assert.True(node.IdentityComplete);
    }

    [Fact]
    public async Task EmptyAndMissingOptionalIdentityAttributesRemainDistinct()
    {
        using var missing = new XmlFixture();
        missing.Replace("index.xml", " dosageform=\"Tablet\"", "");
        var first = await missing.InspectAsync();
        using var empty = new XmlFixture();
        empty.Replace("index.xml", "dosageform=\"Tablet\"", "dosageform=\"\"");
        var second = await empty.InspectAsync();
        var firstNode = Assert.Single(first.Backbones[0].Nodes, node => node.DefinitionKey == "m3-2-p-drug-product");
        var secondNode = Assert.Single(second.Backbones[0].Nodes, node => node.DefinitionKey == "m3-2-p-drug-product");
        Assert.Null(firstNode.IdentityAttributes["dosageform"]);
        Assert.Equal("", secondNode.IdentityAttributes["dosageform"]);
        Assert.NotEqual(firstNode.ContextKey, secondNode.ContextKey);
    }

    [Fact]
    public async Task NonLeafXmlIdsParticipateInTheSameDocumentIndex()
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "<title>Clinical overview</title>", "<title ID=\"alpha-spec-v1\">Clinical overview</title>");
        var inspection = await fixture.InspectAsync();
        var targets = inspection.Backbones[0].IdIndex["alpha-spec-v1"];
        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, target => target.IsLeaf);
        Assert.Contains(targets, target => !target.IsLeaf && target.ElementName == "title");
        Assert.True(inspection.Backbones[0].ReadComplete);
        Assert.Equal(CheckStatus.Fail, inspection.Backbones[0].DtdStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid id")]
    public async Task InvalidIdValuesProduceFindingsInsteadOfCrashing(string id)
    {
        using var fixture = new XmlFixture();
        fixture.Replace("index.xml", "ID=\"qos-alpha\"", "ID=\"" + id + "\"");
        var inspection = await fixture.InspectAsync();
        Assert.Contains(Findings(inspection), finding => finding.RuleId == "XML-IDS" && finding.CheckStatus == CheckStatus.Fail);
    }

    [Fact]
    public async Task MissingReferencedRegionalXmlFailsWhileUnselectedRegionalScopeRemainsUnevaluated()
    {
        using var fixture = new XmlFixture();
        File.Delete(Path.Combine(fixture.Source, "m1/us/us-regional.xml"));
        var selected = await fixture.InspectAsync();
        Assert.Contains(Findings(selected), finding => finding.Code == "BACKBONE_READ_FAILED" && finding.Location.LogicalPath == "0000/m1/us/us-regional.xml");
        var ichOnly = await fixture.InspectAsync(profile: PackageValidationCatalog.IchProfile);
        Assert.Single(ichOnly.Backbones);
        Assert.Contains(Findings(ichOnly), finding => finding.Code == "REGIONAL_PROFILE_REQUIRED" && finding.CheckStatus == CheckStatus.NotEvaluated);
    }

    [Fact]
    public async Task DtdPassingChecksDoNotClaimWholePackageReadiness()
    {
        using var fixture = new XmlFixture();
        await using var input = await fixture.CaptureAsync();
        var inspection = await new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile);
        var binding = new ValidationBinding(input.InputDigest, new string('0', 64), inspection.ProfileSnapshotId,
            inspection.RulesDigest, inspection.EngineVersion, PackageValidationMode.Formal, inspection.LimitsDigest);
        var report = ValidationReport.Create(PackageValidationCatalog.Current, binding, ValidationRunStatus.Completed, inspection.Checks);
        Assert.True(report.ExecutionCompleted);
        Assert.False(report.IsReadyForFinalization);
        Assert.Contains(report.Coverage, coverage => coverage.RuleId == "FILE-MD5" && coverage.CheckStatus == CheckStatus.NotEvaluated);
    }

    [Fact]
    public async Task XmlDeadlineReturnsIncompleteCoverageRatherThanACompletedPass()
    {
        using var fixture = new XmlFixture();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Source, "large.pdf"), new byte[32 * 1024 * 1024]);
        await using var input = await fixture.CaptureAsync();
        var time = new DeadlineTimeProvider();
        var task = new PackageXmlInspector(time).InspectAsync(input, PackageValidationCatalog.UsProfile);
        time.Expire();
        var inspection = await task;
        Assert.Contains(Findings(inspection), finding => finding.Code == "XML_READ_TIMEOUT");
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(inspection.Checks, check => check.RuleId == "XML-IDS").Status);
    }

    [Fact]
    public async Task ChangedInputAndUserCancellationDoNotProduceCompletedInspection()
    {
        using var fixture = new XmlFixture();
        await using var input = await fixture.CaptureAsync();
        fixture.Replace("index.xml", "Drug A", "Drug B");
        await Assert.ThrowsAsync<PackageInputChangedException>(() => new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PackageXmlInspector().InspectAsync(input, PackageValidationCatalog.UsProfile, cancellation.Token));
    }

    private static IEnumerable<ValidationFinding> Findings(PackageXmlInspection inspection) => inspection.Checks.SelectMany(check => check.Findings);

    private sealed class DeadlineTimeProvider : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            return new InertTimer();
        }
        public void Expire() => _callback!(_state);
        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class XmlFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ratools-xml-test-" + Guid.NewGuid().ToString("N"));
        private readonly PackageInputReader _reader;
        public string Source { get; }
        public XmlFixture(string sequence = "0000")
        {
            Source = Path.Combine(_root, "source", sequence);
            Directory.CreateDirectory(Source);
            var original = Path.Combine(PackageRuleCatalogTests.Root, "tests/RATools.Tests/Fixtures/Publisher/sequences", sequence);
            foreach (var file in Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(Source, Path.GetRelativePath(original, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            _reader = new PackageInputReader(Path.Combine(_root, "captures"));
        }
        public void Replace(string path, string old, string replacement)
        {
            var file = Path.Combine(Source, path);
            var text = File.ReadAllText(file);
            Assert.Contains(old, text, StringComparison.Ordinal);
            File.WriteAllText(file, text.Replace(old, replacement, StringComparison.Ordinal), new UTF8Encoding(false));
        }
        public Task<ICapturedPackageInput> CaptureAsync(PackageReadLimits? limits = null) => _reader.ReadAsync(
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "hand-authored", Source), limits ?? new());
        public Task<ICapturedPackageInput> CaptureZipAsync()
        {
            var path = Path.Combine(_root, "delivery.zip");
            ZipFile.CreateFromDirectory(Source, path, CompressionLevel.NoCompression, includeBaseDirectory: true);
            return _reader.ReadAsync(new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "external-zip", path), new());
        }
        public async Task<PackageXmlInspection> InspectAsync(PackageReadLimits? limits = null, string profile = PackageValidationCatalog.UsProfile)
        {
            await using var input = await CaptureAsync(limits);
            return await new PackageXmlInspector().InspectAsync(input, profile);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
