using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaRegulatoryActivityTypeRuleTests
{
    private const string RuleId = "FDA47-2024";

    [Theory]
    [InlineData("grouped-different", CheckStatus.Fail)]
    [InlineData("grouped-current", CheckStatus.Fail)]
    [InlineData("grouped-parent", CheckStatus.Fail)]
    [InlineData("grouped-reordered", CheckStatus.Fail)]
    [InlineData("grouped-match", CheckStatus.NotEvaluated)]
    [InlineData("grouped-member-type", CheckStatus.Fail)]
    [InlineData("grouped-same-number-other-type", CheckStatus.Fail)]
    public async Task GroupedPrimaryComparisonDoesNotMixMemberSequenceNumbers(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        foreach (var xml in fixture.Lifecycle.XmlInspections)
            Assert.Equal(CheckStatus.Pass, Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional).DtdStatus);
        var current = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == fixture.Packages.Target.InputDigest);
        var regional = Assert.Single(current.Backbones, item => item.Kind == BackboneKind.UsRegional);
        var primary = Assert.Single(regional.Elements, item => item.Name == "application" && item.Attribute("application-containing-files") == "true");
        var result = Direct(fixture)!;
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(result.Findings);
            Assert.StartsWith(primary.NodePath + "/", finding.Location.NodePath, StringComparison.Ordinal);
            Assert.Equal("fdast1", finding.Expected);
            Assert.Equal("fdast2", finding.Actual);
            Assert.Equal(2, finding.Location.Files!.Count);
        }
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("grouped-two-primary")]
    [InlineData("grouped-no-primary")]
    [InlineData("grouped-duplicate-identity")]
    [InlineData("grouped-parent-duplicate-identity")]
    [InlineData("grouped-parent-switch")]
    [InlineData("grouped-member-unknown-application-type")]
    [InlineData("grouped-primary-missing-id")]
    public async Task AmbiguousOrChangedPrimaryIdentityIsNotFilledFromAnotherMember(string scenario)
    {
        var fixture = await Fixture(scenario);
        var result = Direct(fixture)!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("2027-01-15", 1, CheckStatus.NotApplicable)]
    [InlineData("2027-01-16", 0, CheckStatus.NotEvaluated)]
    [InlineData("2027-01-16", 1, CheckStatus.Fail)]
    public async Task GroupedPrimaryRequiresTheEffectiveDateAndFindingAllowance(string date, int remaining, CheckStatus expected)
    {
        var check = Direct(await Fixture("grouped-different", date), remaining)!;
        Assert.Equal(expected, check.Status);
        Assert.True(check.Findings.Count <= remaining);
    }

    [Theory]
    [InlineData("grouped-match", CheckStatus.NotEvaluated)]
    [InlineData("grouped-different", CheckStatus.Fail)]
    public async Task GroupedDeliveryDoesNotClaimAllMembersWereCompared(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("match", CheckStatus.Pass)]
    [InlineData("different", CheckStatus.Fail)]
    [InlineData("no-parent", CheckStatus.NotEvaluated)]
    [InlineData("wrong-parent", CheckStatus.NotEvaluated)]
    [InlineData("different-application", CheckStatus.NotEvaluated)]
    [InlineData("different-app-type", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-application", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-id", CheckStatus.NotEvaluated)]
    [InlineData("unknown-type", CheckStatus.NotEvaluated)]
    [InlineData("missing-type", CheckStatus.NotEvaluated)]
    [InlineData("parent-tail", CheckStatus.NotEvaluated)]
    [InlineData("self", CheckStatus.NotEvaluated)]
    [InlineData("unbound-history-context", CheckStatus.NotEvaluated)]
    [InlineData("missing-history-mapping", CheckStatus.NotEvaluated)]
    public async Task SelectedParentComparisonRequiresExactActivityIdentity(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var current = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == fixture.Packages.Target.InputDigest);
        var regional = Assert.Single(current.Backbones, item => item.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        if (scenario is "match" or "different")
        {
            Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
            var parent = fixture.Packages.Sequences["0000"];
            var parentXml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == parent.InputDigest);
            Assert.Equal(CheckStatus.Pass, Assert.Single(parentXml.Backbones, item => item.Kind == BackboneKind.UsRegional).DtdStatus);
            Assert.Single(fixture.Packages.Baseline.Entries);
            Assert.Equal("0001", Assert.Single(regional.Elements, element => element.Name == "sequence-number").Text);
            Assert.Equal("0000", Assert.Single(regional.Elements, element => element.Name == "submission-id").Text);
            var parentRegional = Assert.Single(parentXml.Backbones, item => item.Kind == BackboneKind.UsRegional);
            Assert.Equal("0000", Assert.Single(parentRegional.Elements, element => element.Name == "sequence-number").Text);
            Assert.Equal("0000", Assert.Single(parentRegional.Elements, element => element.Name == "submission-id").Text);
            Assert.Equal(Assert.Single(parentRegional.Elements, element => element.Name == "application-number").Text, Assert.Single(regional.Elements, element => element.Name == "application-number").Text);
        }
        if (scenario == "parent-tail") Assert.False(Assert.Single(Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == fixture.Packages.Sequences["0000"].InputDigest).Backbones, item => item.Kind == BackboneKind.UsRegional).ReadComplete);
        var result = Direct(fixture)!;
        Assert.Equal(expected, result.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
            Assert.Equal("@submission-type", finding.Location.FieldPath);
            Assert.Equal("fdast1", finding.Expected);
            Assert.Equal("fdast2", finding.Actual);
        }
        else Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("2026-11-23", CheckStatus.NotApplicable)]
    [InlineData("2027-01-15", CheckStatus.NotApplicable)]
    [InlineData("2027-01-16", CheckStatus.Fail)]
    [InlineData(null, CheckStatus.NotEvaluated)]
    public async Task FutureEffectiveDateIsExplicit(string? date, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("different", date))!.Status);

    [Theory]
    [InlineData("different", CheckStatus.Fail)]
    [InlineData("match", CheckStatus.Pass)]
    public async Task FileInspectionIncludesTheIndependentRule(string scenario, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        var files = await new PackageFileInspector(standards: fixture.Context).InspectAsync(new(fixture.Packages), fixture.Lifecycle);
        Assert.Equal(expected, Assert.Single(files.Checks, check => check.RuleId == RuleId).Status);
    }

    [Theory]
    [InlineData("duplicate-parent-xml")]
    [InlineData("parent-assets")]
    [InlineData("parent-profile")]
    [InlineData("parent-limits")]
    [InlineData("parent-rules")]
    public async Task ConflictingParentObservationBindingCannotProveMismatch(string scenario)
    {
        var fixture = await Fixture("different");
        var parent = fixture.Packages.Sequences["0000"];
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == parent.InputDigest);
        var replacement = new PackageXmlInspection(xml.InputDigest, scenario == "parent-profile" ? "other" : xml.ProfileSnapshotId,
            scenario == "parent-assets" ? "other" : xml.AssetManifestDigest, scenario == "parent-limits" ? "other" : xml.LimitsDigest,
            xml.Backbones, xml.Checks, scenario == "parent-rules" ? "other" : xml.RulesDigest, xml.StudyTaggingFiles);
        var selected = fixture.Lifecycle.XmlInspections.Where(item => item.InputDigest != parent.InputDigest).Append(replacement);
        if (scenario == "duplicate-parent-xml") selected = selected.Append(replacement);
        fixture = fixture with { Lifecycle = new(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, selected, fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest) };
        Assert.Equal(CheckStatus.NotEvaluated, Direct(fixture)!.Status);
    }

    [Theory]
    [InlineData(0, CheckStatus.NotEvaluated)]
    [InlineData(1, CheckStatus.Fail)]
    public async Task FindingBudgetIsRespected(int remaining, CheckStatus expected)
        => Assert.Equal(expected, Direct(await Fixture("different"), remaining)!.Status);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsProgress(bool afterFailure)
    {
        var fixture = await Fixture("different");
        using var cancellation = new CancellationTokenSource();
        RuleCheckResult? retained = null;
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, progress: check => { retained = check; if (!afterFailure || check.Status == CheckStatus.Fail) cancellation.Cancel(); }, token: cancellation.Token));
        Assert.Equal(afterFailure ? CheckStatus.Fail : CheckStatus.NotEvaluated, retained!.Status);
    }

    [Theory]
    [InlineData("match", CheckStatus.Pass, CheckStatus.NotEvaluated)]
    [InlineData("different", CheckStatus.Fail, CheckStatus.Fail)]
    public async Task PartialReportDoesNotClaimCompleteActivityHistory(string scenario, CheckStatus raw, CheckStatus expected)
    {
        var fixture = await Fixture(scenario);
        Assert.Equal(ImplementationStatus.Partial, fixture.Context.Catalog.Rules[RuleId].ImplementationStatus);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(new(fixture.Packages), PackageValidationCatalog.UsProfile);
        Assert.Equal(raw, Assert.Single(result.Files.Checks, item => item.RuleId == RuleId).Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == RuleId).CheckStatus);
    }

    [Theory]
    [InlineData("reverse-child")]
    [InlineData("field-location")]
    [InlineData("duplicate-regional")]
    public async Task FrozenStructureConflictsRemainUnknown(string scenario)
    {
        var fixture = await Fixture("different");
        var xml = Assert.Single(fixture.Lifecycle.XmlInspections, item => item.InputDigest == fixture.Packages.Target.InputDigest);
        var regional = Assert.Single(xml.Backbones, item => item.Kind == BackboneKind.UsRegional);
        var id = Assert.Single(regional.Elements, item => item.Name == "submission-id");
        var elements = regional.Elements.Select(element => scenario == "reverse-child" && element.NodePath == id.ParentPath
            ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children.Where(path => path != id.NodePath), element.Text, element.Location)
            : scenario == "field-location" && element == id ? new ParsedXmlElement(element.NodePath, element.ParentPath, element.Name, element.LocalName, element.NamespaceUri, element.Attributes.Values, element.Children, element.Text, element.Location with { SequenceNumber = "0000" }) : element);
        var changed = new ParsedBackbone(regional.LogicalPath, regional.ProfileSnapshotId, regional.Kind, regional.ReadComplete, regional.DtdStatus, regional.DocumentTypeName, regional.SystemIdentifier, regional.PublicIdentifier, elements, regional.Nodes, regional.Leaves, regional.IdIndex.Values.SelectMany(ids => ids), regional.ResolvedAssets, regional.ProcessingInstructions);
        var documents = xml.Backbones.Where(document => document != regional).Append(changed);
        if (scenario == "duplicate-regional") documents = documents.Append(changed);
        var replacement = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest, documents, xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, fixture.Lifecycle.XmlInspections.Where(item => item != xml).Append(replacement), fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks, fixture.Context.Catalog.Digest, fixture.Context.Assets.Digest);
        var result = Direct(fixture with { Lifecycle = lifecycle })!;
        Assert.Equal(CheckStatus.NotEvaluated, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task PreCancelledCallPropagates()
    {
        var fixture = await Fixture("different");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Direct(fixture, token: cancellation.Token));
    }

    private static RuleCheckResult? Direct(FixtureData fixture, int remaining = 20, Action<RuleCheckResult>? progress = null, CancellationToken token = default)
        => FdaRegulatoryActivityTypeRule.Inspect(fixture.Packages, fixture.Lifecycle, fixture.Context, remaining, token, progress);
    private sealed record FixtureData(PackageInputSet Packages, PackageLifecycleInspection Lifecycle, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario, string? date = "2027-01-16")
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        var parentContext = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: new DateOnly(2026, 11, 23));
        var context = PackageStandardsContext.FromSnapshot(snapshot, scenario == "missing-history-mapping" ? null : new Dictionary<string, PackageStandardsContext> { ["0000"] = parentContext }, date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        var app = Guid.NewGuid();
        string Regional(string sequence, bool parent)
        {
            var xml = XDocument.Parse(Fda47AttributeRulesTests.Valid());
            xml.Descendants("application-number").Single().SetAttributeValue("application-type", "fdaat1");
            xml.Descendants("sequence-number").Single().Value = sequence;
            var submission = xml.Descendants("submission-id").Single();
            submission.Value = !parent && scenario == "self" ? "0001" : parent && scenario == "wrong-parent" ? "0001" : "0000";
            submission.SetAttributeValue("submission-type", parent || scenario is "match" or "grouped-match" ? "fdast1" : "fdast2");
            if (!parent && scenario == "unknown-type") submission.SetAttributeValue("submission-type", "unqualified");
            if (!parent && scenario == "missing-type") submission.Attribute("submission-type")!.Remove();
            if (parent && scenario == "different-application") xml.Descendants("application-number").Single().Value = "654321";
            if (parent && scenario == "different-app-type") xml.Descendants("application-number").Single().SetAttributeValue("application-type", "fdaat4");
            if (!parent && scenario == "duplicate-id") submission.AddAfterSelf(new XElement(submission));
            if (!parent && scenario == "duplicate-application") { var application = xml.Descendants("application").Single(); application.AddAfterSelf(new XElement(application)); }
            if (scenario.StartsWith("grouped-", StringComparison.Ordinal) && !(scenario == "grouped-current" && parent) && !(scenario == "grouped-parent" && !parent))
            {
                var primary = xml.Descendants("application").Single();
                primary.SetAttributeValue("application-containing-files", "true");
                var member = new XElement(primary);
                member.SetAttributeValue("application-containing-files", "false");
                member.Descendants("application-number").Single().Value = "654321";
                member.Descendants("sequence-number").Single().Value = parent ? "0099" : "0100";
                member.Descendants("submission-id").Single().Value = "0099";
                member.Descendants("submission-id").Single().SetAttributeValue("submission-type", "fdast4");
                if (!parent && scenario == "grouped-two-primary") member.SetAttributeValue("application-containing-files", "true");
                if (!parent && scenario == "grouped-no-primary") primary.SetAttributeValue("application-containing-files", "false");
                if ((!parent && scenario == "grouped-duplicate-identity") || (parent && scenario == "grouped-parent-duplicate-identity"))
                    member.Descendants("application-number").Single().Value = primary.Descendants("application-number").Single().Value;
                if (parent && scenario == "grouped-parent-switch")
                {
                    primary.SetAttributeValue("application-containing-files", "false");
                    member.SetAttributeValue("application-containing-files", "true");
                    member.Descendants("sequence-number").Single().Value = sequence;
                    member.Descendants("submission-id").Single().Value = sequence;
                }
                if (scenario == "grouped-member-type") member.Descendants("submission-id").Single().SetAttributeValue("submission-type", parent ? "fdast1" : "unqualified");
                if (scenario == "grouped-same-number-other-type")
                {
                    member.Descendants("application-number").Single().Value = primary.Descendants("application-number").Single().Value;
                    member.Descendants("application-number").Single().SetAttributeValue("application-type", "fdaat4");
                }
                if (!parent && scenario == "grouped-member-unknown-application-type") member.Descendants("application-number").Single().SetAttributeValue("application-type", "unqualified");
                if (!parent && scenario == "grouped-primary-missing-id") primary.Descendants("submission-id").Single().Remove();
                if (scenario == "grouped-reordered" && !parent) primary.AddBeforeSelf(member);
                else primary.AddAfterSelf(member);
            }
            return xml.ToString();
        }
        var target = new PackageRegionalInspectionTests.MemoryInput(app, "0001", Regional("0001", false), PackageValidationCatalog.UsProfile);
        var parentText = Regional("0000", true);
        if (scenario == "parent-tail") parentText = parentText[..^8];
        var parent = new PackageRegionalInspectionTests.MemoryInput(app, "0000", parentText, PackageValidationCatalog.UsProfile);
        var packages = new PackageInputSet(target, new(app, "0001", scenario == "no-parent" ? [] : [new("0000", HistorySourceKind.ImportedExternal, parent.SourceId, parent.InputDigest, PackageValidationCatalog.UsProfile, HistoryTrustStatus.Unverified, StandardsSnapshotId: scenario == "unbound-history-context" ? null : parentContext.SnapshotId)]), scenario == "no-parent" ? [] : [parent]);
        var lifecycle = await new PackageLifecycleInspector(standards: context).InspectAsync(packages, PackageValidationCatalog.UsProfile);
        return new(packages, lifecycle, context);
    }
}
