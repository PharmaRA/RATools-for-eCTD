using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageLifecycleInspectorTests
{
    private static readonly XNamespace Xlink = "http://www.w3c.org/1999/xlink";

    [Theory]
    [InlineData("0000")]
    [InlineData("0001")]
    [InlineData("0002")]
    [InlineData("0003")]
    public async Task FourSequenceFactsMatchTheIndependentBusinessOracle(string sequence)
    {
        using var fixture = new Fixture();
        var result = await fixture.InspectAsync(sequence);
        Assert.True(result.ResolutionComplete, string.Join('\n', Findings(result).Select(finding => finding.Code + ": " + finding.Message)));
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(PackageRuleCatalogTests.Root, "tests/RATools.Tests/Fixtures/Publisher/expected.json")));
        var effective = expected.RootElement.GetProperty("effectivePayloadLeaves").GetProperty(sequence).EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal);
        Assert.Equal(effective, result.States.Where(state => state.Effectiveness == PackageLeafEffectiveness.Current && !state.IsRegionalReference)
            .Select(state => Key(state.Address)).Order(StringComparer.Ordinal));
        foreach (var item in expected.RootElement.GetProperty("events").EnumerateArray().Where(item => string.CompareOrdinal(item.GetProperty("leaf").GetString()![..4], sequence) <= 0))
        {
            var actual = Assert.Single(result.Events, entry => Key(entry.Source.Address!) == item.GetProperty("leaf").GetString());
            Assert.Equal(item.GetProperty("target").GetString(), Key(actual.Target!));
            Assert.Equal(item.GetProperty("operation").GetString(), actual.Source.Operation);
            Assert.Equal(CheckStatus.Pass, actual.Status);
            Assert.NotNull(actual.TargetContentLogicalPath);
        }
        if (sequence == "0003")
        {
            var deletion = Assert.Single(result.Events, item => item.Source.Id == "beta-delete");
            Assert.Null(deletion.Source.Href);
            Assert.Equal("", deletion.Source.Checksum);
        }
    }

    [Theory]
    [InlineData("../0000/index.xml#missing", "TARGET_LEAF_NOT_FOUND")]
    [InlineData("../0000/m1/us/us-regional.xml#alpha-spec-v1", "TARGET_LEAF_NOT_FOUND")]
    [InlineData("../0000/other.xml#alpha-spec-v1", "TARGET_XML_NOT_FOUND")]
    [InlineData("../0000/index.xml#study-nc-001", "TARGET_IS_NOT_LEAF")]
    [InlineData("../0000/index.xml#beta-spec-v1", "TARGET_CONTEXT_MISMATCH")]
    [InlineData("../0002/index.xml#gamma-spec", "TARGET_IN_FUTURE")]
    [InlineData("../../other/0000/index.xml#alpha-spec-v1", "TARGET_OUTSIDE_APPLICATION")]
    [InlineData("../resend/0000/index.xml#alpha-spec-v1", "TARGET_OUTSIDE_APPLICATION")]
    [InlineData("https://example.invalid/0000/index.xml#alpha-spec-v1", "INVALID_MODIFIED_FILE")]
    [InlineData("../0000/index.xml?copy=2#alpha-spec-v1", "INVALID_MODIFIED_FILE")]
    [InlineData("../0000/index.xml#alpha-spec-v1 beta-spec-v1", "TARGET_OUTSIDE_APPLICATION")]
    [InlineData("../0000/index.xml", "INVALID_MODIFIED_FILE")]
    [InlineData("../0000/index.xml#", "INVALID_MODIFIED_FILE")]
    [InlineData("../0000%2findex.xml#alpha-spec-v1", "INVALID_MODIFIED_FILE")]
    [InlineData("../0000/index.xml#%ZZ", "INVALID_MODIFIED_FILE")]
    [InlineData("", "MODIFIED_FILE_REQUIRED")]
    public async Task ExactReferenceDefectsStayLocatedAndNeverFallBackToFilename(string reference, string code)
    {
        using var fixture = new Fixture();
        fixture.Leaf("0001", "alpha-spec-v2", leaf => leaf.SetAttributeValue("modified-file", reference));
        var result = await fixture.InspectAsync("0001");
        var finding = Assert.Single(Findings(result), finding => finding.Code == code);
        Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
        Assert.Equal("alpha-spec-v2", finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
        Assert.False(result.ResolutionComplete);
        Assert.Equal(PackageLeafEffectiveness.Unknown, Assert.Single(result.States, state => state.Address.LeafId == "alpha-spec-v2").Effectiveness);
    }

    [Fact]
    public async Task MissingHistoryCannotBeAdoptedFromTheSurroundingApplicationDirectory()
    {
        using var fixture = new Fixture();
        var result = await fixture.InspectAsync("0001", history: []);
        Assert.Contains(Findings(result), finding => finding.Code == "HISTORY_NOT_AVAILABLE");
        Assert.DoesNotContain(result.States, state => state.Address.SequenceNumber == "0000");
        Assert.All(result.Checks.Where(check => check.RuleId is "LIFECYCLE-TARGET" or "LIFECYCLE-EFFECTIVE" or "LIFECYCLE-CONTEXT"),
            check => Assert.Equal(CheckStatus.NotEvaluated, check.Status));
    }

    [Theory]
    [InlineData("0002", "alpha-addendum", "../0000/index.xml#alpha-spec-v1")]
    [InlineData("0004", "beta-delete", "../0000/index.xml#beta-spec-v1")]
    [InlineData("0004", "beta-delete", "../0003/index.xml#beta-delete")]
    public async Task AlreadyReplacedDeletedAndDeleteEventTargetsAreRejected(string sequence, string id, string reference)
    {
        using var fixture = new Fixture();
        fixture.Clone("0003", "0004");
        fixture.Leaf(sequence, id, leaf => leaf.SetAttributeValue("modified-file", reference));
        var result = await fixture.InspectAsync(sequence);
        Assert.Contains(Findings(result), finding => finding.Code == "TARGET_NOT_EFFECTIVE" && finding.Location.SequenceNumber == sequence);
    }

    [Fact]
    public async Task DuplicateTargetIdsAreRetainedAndReportedBeforeDtdInvalidity()
    {
        using var fixture = new Fixture();
        fixture.Leaf("0000", "beta-spec-v1", leaf => leaf.SetAttributeValue("ID", "alpha-spec-v1"));
        var result = await fixture.InspectAsync("0001");
        Assert.Contains(Findings(result), finding => finding.Code == "TARGET_ID_AMBIGUOUS");
        Assert.Equal(2, result.Events.Count(item => item.Source.Id == "alpha-spec-v1"));
        Assert.False(result.ResolutionComplete);
    }

    [Fact]
    public async Task EmptyModifiedFileOnNewIsEquivalentToOmission()
    {
        using var fixture = new Fixture();
        fixture.Leaf("0000", "alpha-spec-v1", leaf => leaf.SetAttributeValue("modified-file", ""));
        Assert.True((await fixture.InspectAsync("0000")).ResolutionComplete);
    }

    [Theory]
    [InlineData("checksum", "d41d8cd98f00b204e9800998ecf8427e", "DELETE_CHECKSUM_NOT_EMPTY")]
    [InlineData("href", "new.pdf", "DELETE_HAS_CONTENT")]
    public async Task DeleteChecksDoNotDependOnPresenceOfHref(string attribute, string value, string code)
    {
        using var fixture = new Fixture();
        fixture.Leaf("0003", "beta-delete", leaf => leaf.SetAttributeValue(attribute == "href" ? Xlink + "href" : attribute, value));
        var result = await fixture.InspectAsync("0003");
        var deletion = Assert.Single(result.Events, item => item.Source.Id == "beta-delete");
        Assert.Equal("beta-spec-v1", deletion.Target!.LeafId);
        Assert.Contains(deletion.Findings, finding => finding.Code == code);
    }

    [Fact]
    public async Task RegionalBackboneReferenceMustUseNew()
    {
        using var fixture = new Fixture();
        fixture.Edit("0001/index.xml", xml => xml.Descendants("leaf").First().SetAttributeValue("operation", "replace"));
        var result = await fixture.InspectAsync("0001");
        Assert.Contains(Findings(result), finding => finding.Code == "REGIONAL_REFERENCE_MUST_BE_NEW");
    }

    [Fact]
    public async Task RegionalReferenceResolvesRelativeToItsOwnXmlAndPreservesUnqualifiedContext()
    {
        using var fixture = new Fixture();
        var original = XDocument.Load(fixture.PathOf("0000/m1/us/us-regional.xml")).Root!.Element("m1-regional")!;
        var regional = new XElement(original);
        var leaf = regional.Descendants("leaf").Single();
        leaf.SetAttributeValue("ID", "cover-replacement");
        leaf.SetAttributeValue("operation", "replace");
        leaf.SetAttributeValue("modified-file", "../../../0000/m1/us/us-regional.xml#overview");
        leaf.SetAttributeValue(Xlink + "href", "../../../0000/m1/us/12-cover-letters/cover.pdf");
        fixture.Edit("0001/m1/us/us-regional.xml", xml => xml.Root!.Add(regional));
        var result = await fixture.InspectAsync("0001");
        var replacement = Assert.Single(result.Events, item => item.Source.Id == "cover-replacement");
        Assert.Equal("0000/m1/us/us-regional.xml#overview", Key(replacement.Target!));
        Assert.Equal("0000/m1/us/12-cover-letters/cover.pdf", replacement.TargetContentLogicalPath);
        Assert.Contains(replacement.Findings, finding => finding.Code == "TARGET_CONTEXT_UNRESOLVED");
        Assert.DoesNotContain(replacement.Findings, finding => finding.CheckStatus == CheckStatus.Fail);
    }

    [Fact]
    public async Task HistoricalLeafCanReuseContentFromAnEarlierExplicitSequence()
    {
        using var fixture = new Fixture();
        fixture.Leaf("0001", "alpha-spec-v2", leaf => leaf.SetAttributeValue(Xlink + "href", "../0000/m3/32-body-data/32s-drug-sub/drug-a-alpha/32s4-contr-drug-sub/32s41-spec/specification.pdf"));
        var result = await fixture.InspectAsync("0002");
        var append = Assert.Single(result.Events, item => item.Source.Id == "alpha-addendum");
        Assert.Equal(CheckStatus.Pass, append.Status);
        Assert.StartsWith("0000/", append.TargetContentLogicalPath);
    }

    [Fact]
    public async Task MissingHistoricalContentDoesNotResolveBySameFilenameInOtherNodes()
    {
        using var fixture = new Fixture();
        fixture.Leaf("0000", "alpha-spec-v1", leaf => leaf.SetAttributeValue(Xlink + "href", "missing/specification.pdf"));
        var result = await fixture.InspectAsync("0001");
        Assert.Contains(Findings(result), finding => finding.Code == "TARGET_CONTENT_NOT_FOUND");
    }

    [Fact]
    public async Task DtdInvalidHistoricalXmlCannotProvideAnOtherwiseMatchingTarget()
    {
        using var fixture = new Fixture();
        fixture.Leaf("0000", "qos-alpha", leaf => leaf.SetAttributeValue("checksum-type", null));
        var result = await fixture.InspectAsync("0001");
        Assert.Contains(Findings(result), finding => finding.Code == "TARGET_XML_INVALID");
        Assert.False(result.ResolutionComplete);
    }

    [Fact]
    public async Task IncompleteIntermediateBackboneDoesNotHidePossibleHistoricalOperations()
    {
        using var fixture = new Fixture();
        File.AppendAllText(fixture.PathOf("0001/index.xml"), "<broken>");
        fixture.Leaf("0002", "alpha-addendum", leaf => leaf.SetAttributeValue("modified-file", "../0000/index.xml#alpha-spec-v1"));
        var result = await fixture.InspectAsync("0002");
        Assert.Contains(Findings(result), finding => finding.Code == "TARGET_EFFECTIVENESS_UNKNOWN" && finding.Location.LeafId == "alpha-addendum");
    }

    [Fact]
    public async Task MissingRegionalReferenceIsVisibleEvenWhenDtdAllowsEmptyM1()
    {
        using var fixture = new Fixture();
        fixture.Edit("0000/index.xml", xml => xml.Root!.Element("m1-administrative-information-and-prescribing-information")!.Remove());
        var result = await fixture.InspectAsync("0000");
        Assert.Contains(result.XmlInspections.SelectMany(xml => xml.Checks).SelectMany(check => check.Findings), finding => finding.Code == "REGIONAL_BACKBONE_REFERENCE_MISSING");
        Assert.False(result.ResolutionComplete);
    }

    [Fact]
    public async Task SameSequenceCyclePreservesBothUnresolvedEvents()
    {
        using var fixture = new Fixture();
        fixture.Leaf("0000", "alpha-spec-v1", leaf =>
        {
            leaf.SetAttributeValue("operation", "append");
            leaf.SetAttributeValue("modified-file", "#cycle");
            var other = new XElement(leaf);
            other.SetAttributeValue("ID", "cycle");
            other.SetAttributeValue("modified-file", "#alpha-spec-v1");
            leaf.AddAfterSelf(other);
        });
        var result = await fixture.InspectAsync("0000");
        Assert.Equal(2, result.Events.Count(item => item.Source.Operation == "append" && item.Status == CheckStatus.NotEvaluated));
        Assert.False(result.ResolutionComplete);
    }

    [Fact]
    public async Task FindingLimitRetainsAnExplicitIncompleteTerminalObservation()
    {
        using var fixture = new Fixture();
        fixture.Leaf("0003", "beta-delete", leaf => { leaf.SetAttributeValue("checksum", "not-empty"); leaf.SetAttributeValue(Xlink + "href", "forbidden.pdf"); });
        var result = await fixture.InspectAsync("0003", limits: new() { MaxXmlFindings = 2 });
        Assert.Contains(Findings(result), finding => finding.Code == "LIFECYCLE_LIMIT");
        Assert.True(Findings(result).Count() <= 2);
        Assert.False(result.ResolutionComplete);
    }

    [Theory]
    [InlineData(PackageValidationCatalog.IchProfile, "HISTORY_PROFILE_COMPATIBILITY_UNQUALIFIED")]
    [InlineData("unsupported-profile", "HISTORY_PROFILE_UNSUPPORTED")]
    public async Task ExplicitHistoricalProfileIsNeverSilentlyReplacedByTheCurrentProfile(string historicalProfile, string code)
    {
        using var fixture = new Fixture();
        var result = await fixture.InspectAsync("0001", historicalProfile: historicalProfile);
        Assert.Contains(Findings(result), finding => finding.Code == code);
        Assert.False(result.ResolutionComplete);
    }

    [Theory]
    [InlineData("append", "SAME_SEQUENCE_APPEND_REQUIRES_POLICY")]
    [InlineData("replace", "SAME_SEQUENCE_OPERATION_UNQUALIFIED")]
    [InlineData("delete", "SAME_SEQUENCE_OPERATION_UNQUALIFIED")]
    public async Task SameSequenceOperationsAreExplicitlyUnqualifiedWithoutInventingAnIchAppendBan(string operation, string code)
    {
        using var fixture = new Fixture();
        fixture.Leaf("0000", "alpha-spec-v1", leaf =>
        {
            var other = new XElement(leaf);
            other.SetAttributeValue("ID", "same-sequence");
            other.SetAttributeValue("operation", operation);
            other.SetAttributeValue("modified-file", "#alpha-spec-v1");
            if (operation == "delete") { other.SetAttributeValue(Xlink + "href", null); other.SetAttributeValue("checksum", ""); }
            leaf.AddBeforeSelf(other); // Target appears later in XML.
        });
        var result = await fixture.InspectAsync("0000");
        var item = Assert.Single(result.Events, item => item.Source.Id == "same-sequence");
        Assert.Equal(CheckStatus.NotEvaluated, item.Status);
        Assert.Contains(item.Findings, finding => finding.Code == code);
        Assert.Equal(PackageLeafEffectiveness.Unknown, Assert.Single(result.States, state => state.Address.LeafId == "alpha-spec-v1").Effectiveness);
    }

    [Fact]
    public async Task ConflictsHaveTheSameResultRegardlessOfXmlOrderAndTaintLaterTargets()
    {
        using var fixture = new Fixture();
        fixture.Leaf("0001", "alpha-spec-v2", leaf =>
        {
            var second = new XElement(leaf);
            second.SetAttributeValue("ID", "other-replacement");
            leaf.AddAfterSelf(second);
        });
        var first = await fixture.InspectAsync("0002");
        fixture.Leaf("0001", "alpha-spec-v2", leaf => { var copy = new XElement(leaf); var parent = leaf.Parent!; leaf.Remove(); parent.Add(copy); });
        var secondResult = await fixture.InspectAsync("0002");
        Assert.Equal(first.States, secondResult.States);
        Assert.Equal(Findings(first).Select(finding => (finding.Code, finding.Location.LeafId)), Findings(secondResult).Select(finding => (finding.Code, finding.Location.LeafId)));
        Assert.Equal(2, Findings(first).Count(finding => finding.Code == "SAME_SEQUENCE_CONFLICT"));
        Assert.Contains(Findings(first), finding => finding.Code == "TARGET_EFFECTIVENESS_UNKNOWN" && finding.Location.LeafId == "alpha-addendum");
    }

    [Fact]
    public async Task ComplexAppendBranchIsNotSilentlyCascaded()
    {
        using var fixture = new Fixture();
        fixture.Clone("0001", "0004");
        fixture.Leaf("0004", "alpha-spec-v2", leaf => leaf.SetAttributeValue("modified-file", "../0001/index.xml#alpha-spec-v2"));
        var result = await fixture.InspectAsync("0004");
        Assert.Contains(Findings(result), finding => finding.Code == "APPEND_CHAIN_UNQUALIFIED");
        Assert.False(result.ResolutionComplete);
    }

    [Theory]
    [InlineData(HistoryTrustStatus.Unverified, CheckStatus.NotEvaluated)]
    [InlineData(HistoryTrustStatus.LegacyUnverified, CheckStatus.NotEvaluated)]
    [InlineData(HistoryTrustStatus.Rejected, CheckStatus.Fail)]
    public async Task SelectedBytesDoNotUpgradeHistoricalTrust(HistoryTrustStatus trust, CheckStatus status)
    {
        using var fixture = new Fixture();
        var result = await fixture.InspectAsync("0001", trust: trust);
        Assert.Equal(status, Assert.Single(result.Checks, check => check.RuleId == "HISTORY-BASELINE").Status);
        Assert.False(result.ResolutionComplete);
    }

    [Fact]
    public async Task AggregateLimitsDoNotProduceAPartialPass()
    {
        using var fixture = new Fixture();
        var result = await fixture.InspectAsync("0001", limits: new() { MaxBackbones = 2 });
        Assert.Contains(Findings(result), finding => finding.Code == "LIFECYCLE_LIMIT");
        Assert.All(result.Checks, check => Assert.Equal(CheckStatus.NotEvaluated, check.Status));
    }

    [Fact]
    public async Task ChangedHistoricalBytesAndCancellationCannotProduceReusableResults()
    {
        using var fixture = new Fixture();
        await using var history = await fixture.CaptureAsync("0000", new());
        await using var target = await fixture.CaptureAsync("0001", new());
        var baseline = new HistoryBaselineManifest(target.Manifest.ApplicationId, "0001", [new("0000", HistorySourceKind.ImportedExternal,
            history.SourceId, history.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified)]);
        var inputs = new PackageInputSet(target, baseline, [history]);
        fixture.Leaf("0000", "alpha-spec-v1", leaf => leaf.Element("title")!.Value = "Changed");
        await Assert.ThrowsAsync<PackageInputChangedException>(() => new PackageLifecycleInspector().InspectAsync(inputs, PackageValidationCatalog.UsProfile));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PackageLifecycleInspector().InspectAsync(inputs, PackageValidationCatalog.UsProfile, cancellation.Token));
    }

    private static IEnumerable<ValidationFinding> Findings(PackageLifecycleInspection result) => result.Checks.SelectMany(check => check.Findings);
    private static string Key(LeafAddress address) => address.SequenceNumber + "/" + address.BackboneRelativePath + "#" + address.LeafId;

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ratools-lifecycle-" + Guid.NewGuid().ToString("N"));
        private readonly PackageInputReader _reader;
        public Fixture()
        {
            var original = Path.Combine(PackageRuleCatalogTests.Root, "tests/RATools.Tests/Fixtures/Publisher/sequences");
            Copy(original, Path.Combine(_root, "source"));
            _reader = new PackageInputReader(Path.Combine(_root, "captures"));
        }
        public string PathOf(string relative) => Path.Combine(_root, "source", relative);
        public void Clone(string source, string destination) => Copy(PathOf(source), PathOf(destination));
        private static void Copy(string source, string destination)
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        public void Edit(string relative, Action<XDocument> edit)
        {
            var xml = XDocument.Load(PathOf(relative));
            edit(xml);
            using var writer = new StreamWriter(PathOf(relative), false, new UTF8Encoding(false));
            xml.Save(writer);
        }
        public void Leaf(string sequence, string id, Action<XElement> edit) => Edit(sequence + "/index.xml", xml => edit(xml.Descendants("leaf").Single(leaf => (string?)leaf.Attribute("ID") == id)));
        public Task<ICapturedPackageInput> CaptureAsync(string sequence, PackageReadLimits limits) => _reader.ReadAsync(new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"), "selected-" + sequence, PathOf(sequence)), limits);
        public async Task<PackageLifecycleInspection> InspectAsync(string sequence, string[]? history = null,
            HistoryTrustStatus trust = HistoryTrustStatus.VerifiedForScope, PackageReadLimits? limits = null,
            string historicalProfile = PackageValidationCatalog.UsProfile)
        {
            limits ??= new();
            await using var target = await CaptureAsync(sequence, limits);
            var selected = new List<ICapturedPackageInput>();
            try
            {
                foreach (var number in history ?? Directory.GetDirectories(PathOf("")).Select(Path.GetFileName).Where(number => string.CompareOrdinal(number, sequence) < 0).Select(number => number!).ToArray())
                    selected.Add(await CaptureAsync(number, limits));
                var baseline = new HistoryBaselineManifest(target.Manifest.ApplicationId, sequence, selected.Select(input => new HistoryBaselineEntry(
                    input.Manifest.SequenceNumber, HistorySourceKind.ImportedExternal, input.SourceId, input.InputDigest,
                    historicalProfile, trust, trust == HistoryTrustStatus.VerifiedForScope ? Guid.NewGuid() : null)));
                return await new PackageLifecycleInspector().InspectAsync(new(target, baseline, selected), PackageValidationCatalog.UsProfile);
            }
            finally { foreach (var input in selected) await input.DisposeAsync(); }
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
