using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaActivityStartSequenceRuleTests
{
    private const string RuleId = "FDA47-2025";

    [Theory]
    [InlineData("grouped-match", CheckStatus.Pass, 0)]
    [InlineData("grouped-primary-mismatch", CheckStatus.Fail, 1)]
    [InlineData("grouped-member-mismatch", CheckStatus.Fail, 1)]
    [InlineData("grouped-both-mismatch", CheckStatus.Fail, 2)]
    [InlineData("grouped-reordered", CheckStatus.Fail, 1)]
    public async Task GroupedStartsUseEachApplicationsOwnSequence(string variant, CheckStatus expected, int count)
    {
        var fixture = await Fixture("fdast8", "fdasst1", "fdaat1", "0003", variant: variant);
        Assert.Empty(fixture.Packages.Baseline.Entries);
        var regional = Assert.Single(Assert.Single(fixture.Lifecycle.XmlInspections).Backbones, item => item.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        Assert.Equal(2, regional.Elements.Count(item => item.Name == "application"));
        var check = Direct(fixture)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        foreach (var finding in check.Findings)
        {
            var id = Assert.Single(regional.Elements, item => item.NodePath == finding.Location.NodePath);
            var order = Assert.Single(regional.Elements, item => item.ParentPath == id.ParentPath && item.Name == "sequence-number");
            Assert.Equal(order.Text, finding.Expected);
            Assert.Equal(id.Text, finding.Actual);
            Assert.Equal("0003/m1/us/us-regional.xml", finding.Location.LogicalPath);
            Assert.Equal("submission-id", finding.Location.FieldPath);
        }
    }

    [Theory]
    [InlineData("grouped-two-primary", CheckStatus.NotEvaluated)]
    [InlineData("grouped-no-primary", CheckStatus.NotEvaluated)]
    [InlineData("grouped-duplicate-identity", CheckStatus.NotEvaluated)]
    [InlineData("grouped-primary-wrong-sequence", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-short-sequence", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-short-id", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-unknown-type", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-unknown-subtype", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-presubmission", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-followup", CheckStatus.Pass)]
    [InlineData("grouped-all-followup", CheckStatus.NotApplicable)]
    [InlineData("grouped-primary-fail-member-unknown", CheckStatus.Fail)]
    [InlineData("grouped-member-unknown-first", CheckStatus.Fail)]
    [InlineData("grouped-primary-unknown-member-fail", CheckStatus.Fail)]
    public async Task GroupedIdentityAndMemberUnknownsRemainDistinct(string variant, CheckStatus expected)
    {
        var check = Direct(await Fixture("fdast8", "fdasst1", "fdaat1", "0003", variant: variant))!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(0, 0, CheckStatus.NotEvaluated)]
    [InlineData(1, 1, CheckStatus.Fail)]
    [InlineData(2, 2, CheckStatus.Fail)]
    public async Task GroupedMismatchesShareFindingAllowance(int remaining, int count, CheckStatus expected)
    {
        var check = Direct(await Fixture("fdast8", "fdasst1", "fdaat1", "0003", variant: "grouped-both-mismatch"), remaining)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
    }

    [Theory]
    [InlineData("grouped-match", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-mismatch", CheckStatus.Fail, CheckStatus.Fail)]
    public async Task GroupedDeliveryRetainsPartialCoverage(string variant, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture("fdast8", "fdasst1", "fdaat1", "0003", variant: variant);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("2027-01-15", CheckStatus.NotApplicable)]
    [InlineData("2027-01-16", CheckStatus.Fail)]
    public async Task GroupedComparisonIsNotBackdated(string date, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("fdast8", "fdasst1", "fdaat1", "0003", date: date, variant: "grouped-member-mismatch"))!.Status);

    [Fact]
    public async Task GroupedCancellationRetainsTheFirstImmutableMismatch()
    {
        var fixture = await Fixture("fdast8", "fdasst1", "fdaat1", "0003", variant: "grouped-both-mismatch");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, progress: check =>
        {
            retained = check;
            if (check.Status == CheckStatus.Fail) cancellation.Cancel();
        }, token: cancellation.Token));
        Assert.Equal(CheckStatus.Fail, retained!.Status);
        Assert.Single(retained.Findings);
    }

    [Fact]
    public async Task FrozenWorkBudgetRetainsKnownMismatchAfterLaterExhaustion()
    {
        var fixture = await Fixture("fdast8", "fdasst1", "fdaat1", "0003", variant: "grouped-both-mismatch");
        var low = 1;
        var high = fixture.Packages.Target.Limits.MaxXmlNodes;
        Assert.Equal(2, Direct(WithWorkBudget(fixture, high))!.Findings.Count);
        // Rebind already frozen observations: this exercises rule work, not XML parser limits.
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Direct(WithWorkBudget(fixture, middle))!.Findings.Count > 0) high = middle;
            else low = middle + 1;
        }
        Assert.True(low > 1);
        var before = Direct(WithWorkBudget(fixture, low - 1))!;
        Assert.Equal(CheckStatus.NotEvaluated, before.Status);
        Assert.Empty(before.Findings);
        RuleCheckResult? progress = null;
        var after = Direct(WithWorkBudget(fixture, low), progress: result => progress = result)!;
        Assert.Equal(CheckStatus.Fail, after.Status);
        Assert.Single(after.Findings);
        Assert.Contains("work allowance was exhausted", after.Reason, StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Fail, progress!.Status);
        Assert.Single(progress.Findings);
        Assert.Equal(after.Findings[0], progress.Findings[0]);
    }

    private static FixtureData WithWorkBudget(FixtureData fixture, int nodes)
    {
        var target = new FrozenWorkInput(fixture.Packages.Target, fixture.Packages.Target.Limits with { MaxXmlNodes = nodes });
        var packages = new PackageInputSet(target, fixture.Packages.Baseline, []);
        var xmls = fixture.Lifecycle.XmlInspections.Select(xml => new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId,
            xml.AssetManifestDigest, target.Limits.Digest(), xml.Backbones, xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles));
        var lifecycle = new PackageLifecycleInspection(packages, fixture.Lifecycle.ProfileSnapshotId, xmls, fixture.Lifecycle.Events,
            fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        return new(packages, lifecycle, fixture.Context);
    }

    private sealed class FrozenWorkInput(ICapturedPackageInput original, PackageReadLimits limits) : ICapturedPackageInput
    {
        public string SourceId => original.SourceId;
        public PackageSourceKind SourceKind => original.SourceKind;
        public PackageInputManifest Manifest => original.Manifest;
        public string? ArchiveSha256 => original.ArchiveSha256;
        public string InputDigest => original.InputDigest;
        public PackageReadLimits Limits => limits;
        public Stream OpenRead(string path) => throw new InvalidOperationException("Work-budget replay must not reopen captured files.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Work-budget replay uses frozen observations only.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("fdast5", "fdasst6", "fdaat1", true)]
    [InlineData("fdast5", "fdasst6", "fdaat1", false)]
    [InlineData("fdast9", "fdasst6", "fdaat4", true)]
    [InlineData("fdast9", "fdasst6", "fdaat4", false)]
    [InlineData("fdast10", "fdasst6", "fdaat1", true)]
    [InlineData("fdast10", "fdasst6", "fdaat1", false)]
    [InlineData("fdast7", "fdasst1", "fdaat1", true)]
    [InlineData("fdast7", "fdasst1", "fdaat1", false)]
    [InlineData("fdast8", "fdasst1", "fdaat1", true)]
    [InlineData("fdast8", "fdasst1", "fdaat1", false)]
    [InlineData("fdast6", "fdasst7", "fdaat1", true)]
    [InlineData("fdast6", "fdasst7", "fdaat1", false)]
    [InlineData("fdast11", "fdasst3", "fdaat1", true)]
    [InlineData("fdast11", "fdasst3", "fdaat1", false)]
    public async Task QualifiedStartCombinationsCompareWithoutAnyHistory(string type, string subtype, string applicationType, bool matches)
    {
        var fixture = await Fixture(type, subtype, applicationType, matches ? "0003" : "0001");
        Assert.Empty(fixture.Packages.Baseline.Entries);
        var regional = Assert.Single(Assert.Single(fixture.Lifecycle.XmlInspections).Backbones, document => document.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        Assert.Equal(type, Assert.Single(regional.Elements, element => element.Name == "submission-id").Attribute("submission-type"));
        Assert.Equal(subtype, Assert.Single(regional.Elements, element => element.Name == "sequence-number").Attribute("submission-sub-type"));
        var check = Direct(fixture)!;
        Assert.Equal(matches ? CheckStatus.Pass : CheckStatus.Fail, check.Status);
        if (matches) Assert.Empty(check.Findings);
        else
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal("0003", finding.Expected);
            Assert.Equal("0001", finding.Actual);
            Assert.Equal("submission-id", finding.Location.FieldPath);
            Assert.Equal("0003/m1/us/us-regional.xml", finding.Location.LogicalPath);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
        }
    }

    [Theory]
    [InlineData("fdast1", "fdasst2", "0002", "0001")]
    [InlineData("fdast1", "fdasst3", "0003", "0001")]
    [InlineData("fdast2", "fdasst3", "0003", "0001")]
    [InlineData("fdast3", "fdasst3", "0003", "0001")]
    [InlineData("fdast4", "fdasst3", "0003", "0001")]
    [InlineData("fdast1", "fdasst3", "0003", "0003")]
    public async Task PresubmissionAndApplicationDoNotProveFirstSubmission(string type, string subtype, string sequence, string submission)
    {
        var fixture = await Fixture(type, subtype, "fdaat1", submission, sequence: sequence);
        var check = Direct(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("fdast5", "fdasst4")]
    [InlineData("fdast8", "fdasst5")]
    [InlineData("fdast11", "fdasst4")]
    public async Task QualifiedFollowupsDoNotImposeStartEquality(string type, string subtype)
        => Assert.Equal(CheckStatus.NotApplicable, Direct(await Fixture(type, subtype, "fdaat1", "0001"))!.Status);

    [Theory]
    [InlineData("bad-combination")]
    [InlineData("unknown-type")]
    [InlineData("unknown-subtype")]
    [InlineData("duplicate-submission")]
    [InlineData("duplicate-application")]
    [InlineData("bad-sequence")]
    [InlineData("bad-id")]
    [InlineData("same-number-short-id")]
    [InlineData("missing-policy")]
    [InlineData("missing-m1")]
    [InlineData("missing-criteria")]
    public async Task UnqualifiedFactsCannotAuthorizeEqualityComparison(string variant)
    {
        var fixture = await Fixture("fdast5", "fdasst6", "fdaat1", "0001", variant: variant);
        var check = Direct(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2027-01-15", CheckStatus.NotApplicable)]
    [InlineData("2027-01-16", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task EffectiveDateIsExplicit(string? date, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("fdast5", "fdasst6", "fdaat1", "0001", date: date))!.Status);

    [Theory]
    [InlineData(true, CheckStatus.Pass)]
    [InlineData(false, CheckStatus.Fail)]
    public async Task FileInspectorIncludesTheIndependentRule(bool matches, CheckStatus expected)
    {
        var fixture = await Fixture("fdast5", "fdasst6", "fdaat1", matches ? "0003" : "0001");
        var result = await new PackageFileInspector(standards: fixture.Context).InspectAsync(new(fixture.Packages), fixture.Lifecycle);
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingAllowanceIsExact(int remaining, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("fdast5", "fdasst6", "fdaat1", "0001"), remaining)!.Status);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsProgress(bool afterFailure)
    {
        var fixture = await Fixture("fdast5", "fdasst6", "fdaat1", "0001");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, progress: check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }, token: cancellation.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("fdasst6", "0003", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("fdasst6", "0001", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("fdasst4", "0001", CheckStatus.NotApplicable, CheckStatus.NotApplicable)]
    public async Task PartialReportPreservesUnknownStartQualification(string subtype, string id, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture("fdast5", subtype, "fdaat1", id);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, check => check.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("reverse-child")]
    [InlineData("wrong-location")]
    [InlineData("duplicate-xml")]
    [InlineData("wrong-rules")]
    public async Task FrozenIdentityConflictsCannotAuthorizeComparison(string variant)
    {
        var fixture = await Fixture("fdast5", "fdasst6", "fdaat1", "0001");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var regional = Assert.Single(xml.Backbones, document => document.Kind == BackboneKind.UsRegional);
        var id = Assert.Single(regional.Elements, element => element.Name == "submission-id");
        var elements = regional.Elements.Select(element => variant == "reverse-child" && element.NodePath == id.ParentPath
            ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children.Where(path => path != id.NodePath), element.Text, element.Location)
            : variant == "wrong-location" && element == id ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children, element.Text, element.Location with { SequenceNumber = "0001" }) : element);
        if (variant == "duplicate-id") elements = elements.Append(id);
        var changed = new ParsedBackbone(regional.LogicalPath, regional.ProfileSnapshotId, regional.Kind, true, CheckStatus.Pass, regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier, elements, regional.Nodes, regional.Leaves, regional.IdIndex.Values.SelectMany(ids => ids), regional.ResolvedAssets, regional.ProcessingInstructions);
        var replacement = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones.Where(document => document != regional).Append(changed), xml.Checks, variant == "wrong-rules" ? "other" : xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, variant == "duplicate-xml" ? [replacement, replacement] : [replacement], fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var check = Direct(fixture with { Lifecycle = lifecycle })!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("fdast5", "fdasst6", "fdaat1", "0001");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static RuleCheckResult? Direct(FixtureData fixture, int remaining = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaActivityStartSequenceRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, remaining, token, progress);
    private static async Task<FixtureData> Fixture(string type, string subtype, string applicationType, string submission, string sequence = "0003", string? date = "2027-01-16", string variant = "valid")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        var hash = variant == "missing-policy" ? FdaM1AttributePolicy.Digest : variant == "missing-m1" ? "1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2" : variant == "missing-criteria" ? "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" : null;
        if (hash is not null)
        {
            Assert.Contains(snapshot.Resources, resource => resource.Sha256 == hash);
            snapshot = StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != hash).ToArray() }, snapshot.Resources.Where(item => item.Sha256 != hash), snapshot.AcquiredUtc);
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var xml = XDocument.Parse(Fda47AttributeRulesTests.Valid());
        xml.Descendants("application-number").Single().SetAttributeValue("application-type", applicationType);
        var id = xml.Descendants("submission-id").Single();
        id.Value = variant == "bad-id" ? "1" : variant == "same-number-short-id" ? "3" : submission;
        id.SetAttributeValue("submission-type", variant == "unknown-type" ? "invalid" : type);
        var order = xml.Descendants("sequence-number").Single();
        order.Value = variant == "bad-sequence" ? "0002" : sequence;
        order.SetAttributeValue("submission-sub-type", variant == "unknown-subtype" ? "invalid" : variant == "bad-combination" ? "fdasst3" : subtype);
        if (variant == "duplicate-submission") { var info = xml.Descendants("submission-information").Single(); info.AddAfterSelf(new XElement(info)); }
        if (variant == "duplicate-application") { var app = xml.Descendants("application").Single(); app.AddAfterSelf(new XElement(app)); }
        if (variant.StartsWith("grouped-", StringComparison.Ordinal))
        {
            var primary = xml.Descendants("application").Single();
            primary.SetAttributeValue("application-containing-files", "true");
            var member = new XElement(primary);
            member.SetAttributeValue("application-containing-files", "false");
            member.Descendants("application-number").Single().Value = "654321";
            member.Descendants("sequence-number").Single().Value = "0042";
            member.Descendants("submission-id").Single().Value = variant is "grouped-member-mismatch" or "grouped-both-mismatch" or "grouped-reordered" ? "0003" : "0042";
            if (variant is "grouped-primary-mismatch" or "grouped-both-mismatch") id.Value = "0001";
            var memberId = member.Descendants("submission-id").Single();
            var memberOrder = member.Descendants("sequence-number").Single();
            if (variant == "grouped-two-primary") member.SetAttributeValue("application-containing-files", "true");
            if (variant == "grouped-no-primary") primary.SetAttributeValue("application-containing-files", "false");
            if (variant == "grouped-duplicate-identity") member.Descendants("application-number").Single().Value = primary.Descendants("application-number").Single().Value;
            if (variant == "grouped-primary-wrong-sequence") order.Value = "0042";
            if (variant == "grouped-member-short-sequence") memberOrder.Value = "42";
            if (variant == "grouped-member-short-id") memberId.Value = "42";
            if (variant == "grouped-member-unknown-type") memberId.SetAttributeValue("submission-type", "unqualified");
            if (variant == "grouped-member-unknown-subtype") memberOrder.SetAttributeValue("submission-sub-type", "unqualified");
            if (variant == "grouped-member-presubmission") { memberId.SetAttributeValue("submission-type", "fdast1"); memberOrder.SetAttributeValue("submission-sub-type", "fdasst2"); }
            if (variant is "grouped-member-followup" or "grouped-all-followup") { memberOrder.SetAttributeValue("submission-sub-type", "fdasst4"); memberId.Value = "0040"; }
            if (variant == "grouped-all-followup") { order.SetAttributeValue("submission-sub-type", "fdasst4"); id.Value = "0001"; }
            if (variant is "grouped-primary-fail-member-unknown" or "grouped-member-unknown-first") { id.Value = "0001"; memberId.SetAttributeValue("submission-type", "unqualified"); }
            if (variant == "grouped-primary-unknown-member-fail") { id.SetAttributeValue("submission-type", "unqualified"); memberId.Value = "0003"; }
            if (variant is "grouped-reordered" or "grouped-member-unknown-first") primary.AddBeforeSelf(member);
            else primary.AddAfterSelf(member);
        }
        var target = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), sequence, xml.ToString(), PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, sequence, []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        return new(packages, lifecycle, context);
    }
}
