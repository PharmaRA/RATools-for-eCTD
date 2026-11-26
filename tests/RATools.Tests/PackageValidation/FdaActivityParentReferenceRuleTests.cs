using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaActivityParentReferenceRuleTests
{
    private const string RuleId = "FDA47-2026";

    [Theory]
    [InlineData("grouped-earlier", CheckStatus.Pass, 0)]
    [InlineData("grouped-primary-self", CheckStatus.Fail, 1)]
    [InlineData("grouped-member-self", CheckStatus.Fail, 1)]
    [InlineData("grouped-both-self", CheckStatus.Fail, 2)]
    [InlineData("grouped-reordered", CheckStatus.Fail, 1)]
    [InlineData("grouped-directory-id", CheckStatus.Pass, 0)]
    public async Task GroupedFollowupsCompareAgainstEachMembersOwnSequence(string variant, CheckStatus expected, int count)
    {
        var fixture = await Fixture("fdast8", "fdasst4", "0001", variant: variant);
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
            Assert.Equal(order.Text, id.Text);
            Assert.Equal(order.Text, finding.Actual);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0003/m1/us/us-regional.xml", finding.Location.LogicalPath);
        }
    }

    [Theory]
    [InlineData("grouped-two-primary", CheckStatus.NotEvaluated)]
    [InlineData("grouped-no-primary", CheckStatus.NotEvaluated)]
    [InlineData("grouped-duplicate-identity", CheckStatus.NotEvaluated)]
    [InlineData("grouped-primary-wrong-sequence", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-short-sequence", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-short-id", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-future", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-unknown-type", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-unknown-subtype", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-start", CheckStatus.NotEvaluated)]
    [InlineData("grouped-all-start", CheckStatus.NotEvaluated)]
    [InlineData("grouped-primary-self-member-unknown", CheckStatus.Fail)]
    [InlineData("grouped-member-unknown-first", CheckStatus.Fail)]
    [InlineData("grouped-primary-unknown-member-self", CheckStatus.Fail)]
    [InlineData("grouped-primary-self-member-future", CheckStatus.Fail)]
    public async Task GroupedIdentityAndUnqualifiedMembersDoNotEraseKnownSelfReference(string variant, CheckStatus expected)
    {
        var check = Direct(await Fixture("fdast8", "fdasst4", "0001", variant: variant))!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }

    [Theory]
    [InlineData(0, 0, CheckStatus.NotEvaluated)]
    [InlineData(1, 1, CheckStatus.Fail)]
    [InlineData(2, 2, CheckStatus.Fail)]
    public async Task GroupedSelfReferencesShareFindingAllowance(int remaining, int count, CheckStatus expected)
    {
        var check = Direct(await Fixture("fdast8", "fdasst4", "0001", variant: "grouped-both-self"), remaining)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
    }

    [Theory]
    [InlineData("grouped-directory-id", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-self", CheckStatus.Fail, CheckStatus.Fail)]
    public async Task GroupedDeliveryDoesNotProveParentExistence(string variant, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture("fdast8", "fdasst4", "0001", variant: variant);
        Assert.Empty(fixture.Packages.Baseline.Entries);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("2027-01-15", CheckStatus.NotApplicable)]
    [InlineData("2027-01-16", CheckStatus.Fail)]
    public async Task GroupedComparisonIsNotBackdated(string date, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("fdast8", "fdasst4", "0001", date: date, variant: "grouped-member-self"))!.Status);

    [Fact]
    public async Task GroupedCancellationRetainsTheFirstImmutableSelfReference()
    {
        var fixture = await Fixture("fdast8", "fdasst4", "0001", variant: "grouped-both-self");
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
    public async Task FrozenWorkBudgetRetainsSelfReferenceAfterLaterExhaustion()
    {
        var fixture = await Fixture("fdast8", "fdasst4", "0001", variant: "grouped-both-self");
        var low = 1; var high = fixture.Packages.Target.Limits.MaxXmlNodes;
        Assert.Equal(2, Direct(WithWorkBudget(fixture, high))!.Findings.Count);
        // Rebind frozen facts to test rule work, not XML parsing under reduced limits.
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
        RuleCheckResult? retained = null;
        var after = Direct(WithWorkBudget(fixture, low), progress: check => retained = check)!;
        Assert.Equal(CheckStatus.Fail, after.Status);
        Assert.Single(after.Findings);
        Assert.Contains("work allowance was exhausted", after.Reason, StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Fail, retained!.Status);
        Assert.Single(retained.Findings);
        Assert.Equal(after.Findings[0], retained.Findings[0]);
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
    [InlineData("fdast5", "fdasst4", "0003", CheckStatus.Fail)]
    [InlineData("fdast5", "fdasst4", "0001", CheckStatus.Pass)]
    [InlineData("fdast8", "fdasst5", "0003", CheckStatus.Fail)]
    [InlineData("fdast8", "fdasst5", "0001", CheckStatus.Pass)]
    [InlineData("fdast11", "fdasst4", "0003", CheckStatus.Fail)]
    [InlineData("fdast11", "fdasst5", "0001", CheckStatus.Pass)]
    [InlineData("fdast5", "fdasst4", "0004", CheckStatus.NotEvaluated)]
    [InlineData("fdast1", "fdasst2", "0003", CheckStatus.NotEvaluated)]
    [InlineData("fdast1", "fdasst2", "0001", CheckStatus.NotEvaluated)]
    [InlineData("fdast1", "fdasst3", "0003", CheckStatus.NotEvaluated)]
    [InlineData("fdast1", "fdasst3", "0001", CheckStatus.NotEvaluated)]
    public async Task FollowupSelfReferenceIsNotConfusedWithUnknownStarts(string type, string subtype, string id, CheckStatus expected)
    {
        var fixture = await Fixture(type, subtype, id);
        Assert.Empty(fixture.Packages.Baseline.Entries);
        var regional = Assert.Single(Assert.Single(fixture.Lifecycle.XmlInspections).Backbones, document => document.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        Assert.Equal("0003", Assert.Single(regional.Elements, element => element.Name == "sequence-number").Text);
        Assert.Equal(subtype, Assert.Single(regional.Elements, element => element.Name == "sequence-number").Attribute("submission-sub-type"));
        Assert.Equal(id, Assert.Single(regional.Elements, element => element.Name == "submission-id").Text);
        Assert.Equal(type, Assert.Single(regional.Elements, element => element.Name == "submission-id").Attribute("submission-type"));
        Assert.Equal("fdaat1", Assert.Single(regional.Elements, element => element.Name == "application-number").Attribute("application-type"));
        var check = Direct(fixture)!;
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Equal("0003", finding.Actual);
            Assert.Equal("submission-id", finding.Location.FieldPath);
            Assert.Equal("0003/m1/us/us-regional.xml", finding.Location.LogicalPath);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("bad-combination")]
    [InlineData("unknown-subtype")]
    [InlineData("unknown-type")]
    [InlineData("short-id")]
    [InlineData("empty-id")]
    [InlineData("missing-id-element")]
    [InlineData("wrong-sequence")]
    [InlineData("duplicate-application")]
    [InlineData("duplicate-submission")]
    [InlineData("missing-policy")]
    [InlineData("missing-m1")]
    [InlineData("missing-criteria")]
    public async Task InvalidOrAmbiguousInputsDoNotProveSelfReference(string variant)
    {
        var fixture = await Fixture("fdast5", "fdasst4", "0003", variant: variant);
        if (variant == "bad-combination")
        {
            Assert.Contains("fdast5", fixture.Context.FdaM1Policy!.InputValues("submission-type"));
            Assert.Contains("fdasst5", fixture.Context.FdaM1Policy.InputValues("submission-sub-type"));
            Assert.False(fixture.Context.FdaM1Policy.AllowsCombination("fdaat1", "fdast5", "fdasst5"));
        }
        var check = Direct(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("2027-01-15", CheckStatus.NotApplicable)]
    [InlineData("2027-01-16", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task FutureEffectiveDateIsRespected(string? date, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("fdast5", "fdasst4", "0003", date: date))!.Status);

    [Theory]
    [InlineData("0003", CheckStatus.Fail)]
    [InlineData("0001", CheckStatus.Pass)]
    public async Task FileInspectionIncludesIndependentParentReferenceCheck(string id, CheckStatus expected)
    {
        var fixture = await Fixture("fdast5", "fdasst4", id);
        var result = await new PackageFileInspector(standards: fixture.Context).InspectAsync(new(fixture.Packages), fixture.Lifecycle);
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingBudgetIsExact(int remaining, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("fdast5", "fdasst4", "0003"), remaining)!.Status);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsProgress(bool afterFailure)
    {
        var fixture = await Fixture("fdast5", "fdasst4", "0003");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, progress: check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }, token: cancellation.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("0003", CheckStatus.Fail, CheckStatus.Fail)]
    [InlineData("0001", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("0004", CheckStatus.NotEvaluated, CheckStatus.NotEvaluated)]
    public async Task PartialReportDoesNotProveParentExistence(string id, CheckStatus raw, CheckStatus coverage)
    {
        var fixture = await Fixture("fdast5", "fdasst4", id);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        Assert.Empty(fixture.Packages.Baseline.Entries);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(coverage, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("reverse-child")]
    [InlineData("wrong-location")]
    [InlineData("duplicate-xml")]
    [InlineData("wrong-assets")]
    [InlineData("incomplete")]
    public async Task FrozenIdentityOrStructureConflictsCannotProveSelfReference(string variant)
    {
        var fixture = await Fixture("fdast5", "fdasst4", "0003");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections);
        var regional = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        var id = Assert.Single(regional.Elements, item => item.Name == "submission-id");
        var elements = regional.Elements.Select(element => variant == "reverse-child" && element.NodePath == id.ParentPath
            ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children.Where(path => path != id.NodePath), element.Text, element.Location)
            : variant == "wrong-location" && element == id ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children, element.Text, element.Location with { SequenceNumber = "0001" }) : element);
        var changed = new ParsedBackbone(regional.LogicalPath, regional.ProfileSnapshotId, regional.Kind, variant != "incomplete", regional.DtdStatus, regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier, elements, regional.Nodes, regional.Leaves, regional.IdIndex.Values.SelectMany(ids => ids), regional.ResolvedAssets, regional.ProcessingInstructions);
        var replacement = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, variant == "wrong-assets" ? "other" : xml.AssetManifestDigest, xml.LimitsDigest, xml.Backbones.Where(item => item != regional).Append(changed), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, variant == "duplicate-xml" ? [replacement, replacement] : [replacement], fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var check = Direct(fixture with { Lifecycle = lifecycle })!;
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("fdast5", "fdasst4", "0003");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static RuleCheckResult? Direct(FixtureData fixture, int remaining = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaActivityParentReferenceRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, remaining, token, progress);
    private static async Task<FixtureData> Fixture(string type, string subtype, string idValue, string? date = "2027-01-16", string variant = "valid")
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
        xml.Descendants("application-number").Single().SetAttributeValue("application-type", "fdaat1");
        var id = xml.Descendants("submission-id").Single();
        id.Value = variant == "short-id" ? "3" : variant == "empty-id" ? "" : idValue;
        id.SetAttributeValue("submission-type", variant == "unknown-type" ? "invalid" : type);
        if (variant == "missing-id-element") id.Remove();
        var order = xml.Descendants("sequence-number").Single();
        order.Value = variant == "wrong-sequence" ? "0002" : "0003";
        order.SetAttributeValue("submission-sub-type", variant == "unknown-subtype" ? "invalid" : variant == "bad-combination" ? "fdasst5" : subtype);
        if (variant == "duplicate-application") { var app = xml.Descendants("application").Single(); app.AddAfterSelf(new XElement(app)); }
        if (variant == "duplicate-submission") { var submission = xml.Descendants("submission-information").Single(); submission.AddAfterSelf(new XElement(submission)); }
        if (variant.StartsWith("grouped-", StringComparison.Ordinal))
        {
            var primary = xml.Descendants("application").Single();
            primary.SetAttributeValue("application-containing-files", "true");
            var member = new XElement(primary);
            member.SetAttributeValue("application-containing-files", "false");
            member.Descendants("application-number").Single().Value = "654321";
            member.Descendants("sequence-number").Single().Value = "0042";
            member.Descendants("submission-id").Single().Value = variant is "grouped-member-self" or "grouped-both-self" or "grouped-reordered" ? "0042" :
                variant == "grouped-directory-id" ? "0003" : "0040";
            if (variant is "grouped-primary-self" or "grouped-both-self") id.Value = "0003";
            var memberId = member.Descendants("submission-id").Single();
            var memberOrder = member.Descendants("sequence-number").Single();
            if (variant == "grouped-two-primary") member.SetAttributeValue("application-containing-files", "true");
            if (variant == "grouped-no-primary") primary.SetAttributeValue("application-containing-files", "false");
            if (variant == "grouped-duplicate-identity") member.Descendants("application-number").Single().Value = primary.Descendants("application-number").Single().Value;
            if (variant == "grouped-primary-wrong-sequence") order.Value = "0042";
            if (variant == "grouped-member-short-sequence") memberOrder.Value = "42";
            if (variant == "grouped-member-short-id") memberId.Value = "42";
            if (variant == "grouped-member-future") memberId.Value = "0043";
            if (variant == "grouped-member-unknown-type") memberId.SetAttributeValue("submission-type", "unqualified");
            if (variant == "grouped-member-unknown-subtype") memberOrder.SetAttributeValue("submission-sub-type", "unqualified");
            if (variant is "grouped-member-start" or "grouped-all-start") memberOrder.SetAttributeValue("submission-sub-type", "fdasst1");
            if (variant == "grouped-all-start") order.SetAttributeValue("submission-sub-type", "fdasst1");
            if (variant is "grouped-primary-self-member-unknown" or "grouped-member-unknown-first") { id.Value = "0003"; memberId.SetAttributeValue("submission-type", "unqualified"); }
            if (variant == "grouped-primary-unknown-member-self") { id.SetAttributeValue("submission-type", "unqualified"); memberId.Value = "0042"; }
            if (variant == "grouped-primary-self-member-future") { id.Value = "0003"; memberId.Value = "0043"; }
            if (variant is "grouped-reordered" or "grouped-member-unknown-first") primary.AddBeforeSelf(member);
            else primary.AddAfterSelf(member);
        }
        var target = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0003", xml.ToString(), PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(target.Manifest.ApplicationId, "0003", []), []);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        return new(packages, lifecycle, context);
    }
}
