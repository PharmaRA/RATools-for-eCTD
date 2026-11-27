using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaTrialSummaryRegionBindingTests
{
    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("historical-path", CheckStatus.NotEvaluated)]
    [InlineData("uncaptured-path", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("wrong-location", CheckStatus.NotEvaluated)]
    [InlineData("wrong-node-location", CheckStatus.NotEvaluated)]
    [InlineData("extra-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root", CheckStatus.NotEvaluated)]
    [InlineData("wrong-root-namespace", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-element", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-region", CheckStatus.NotEvaluated)]
    [InlineData("unread-region", CheckStatus.NotEvaluated)]
    [InlineData("invalid-dtd", CheckStatus.NotEvaluated)]
    [InlineData("reordered", CheckStatus.Fail)]
    public async Task ApplicationTypeRequiresTheCurrentCapturedRegionalObservation(string scenario, CheckStatus expected)
    {
        var fixture = await FdaTrialSummaryRequirementRuleTests.Fixture("missing", "m4.2.3.1");
        var original = Assert.Single(fixture.Lifecycle.XmlInspections);
        var regional = Assert.Single(original.Backbones, document => document.Kind == BackboneKind.UsRegional);
        var elements = regional.Elements.Select(element => new ParsedXmlElement(element.NodePath, element.ParentPath,
            element.Name, scenario == "wrong-root" && element.ParentPath is null ? "other" : element.LocalName,
            scenario == "wrong-root-namespace" && element.ParentPath is null ? "urn:other" : element.NamespaceUri,
            element.Attributes.Values, element.Children, element.Text,
            element.Location with { LogicalPath = scenario == "wrong-location" ? "0000/m1/us/us-regional.xml" : element.Location.LogicalPath,
                NodePath = scenario == "wrong-node-location" ? "/other" : element.Location.NodePath })).ToList();
        if (scenario == "duplicate-element") elements.Add(elements[0]);
        if (scenario == "extra-root") elements.Add(new("/other", null, "other", "other", "", [], [], "",
            new("0001", regional.LogicalPath, NodePath: "/other")));
        if (scenario == "reordered") elements.Reverse();
        var replacement = new ParsedBackbone(scenario == "historical-path" ? "0000/m1/us/us-regional.xml" :
            scenario == "uncaptured-path" ? "0001/m1/us/absent.xml" : regional.LogicalPath,
            scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : regional.ProfileSnapshotId,
            regional.Kind, scenario != "unread-region" && regional.ReadComplete,
            scenario == "invalid-dtd" ? CheckStatus.Fail : regional.DtdStatus, "fda-regional:fda-regional", null, null,
            elements, [], [], [], []);
        var backbones = original.Backbones.Select(document => document == regional ? replacement : document).ToList();
        if (scenario == "duplicate-region") backbones.Add(replacement);
        var xml = new PackageXmlInspection(original.InputDigest, original.ProfileSnapshotId, original.AssetManifestDigest,
            original.LimitsDigest, backbones,
            original.Checks, original.RulesDigest, original.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(fixture.Packages, fixture.Lifecycle.ProfileSnapshotId, [xml],
            fixture.Lifecycle.Events, fixture.Lifecycle.States, fixture.Lifecycle.Checks,
            fixture.Lifecycle.RulesDigest, fixture.Lifecycle.AssetManifestDigest);
        var data = await PackageStudyDataInspector.InspectAsync(fixture.Packages, lifecycle, fixture.Context);
        Assert.True(data.ScopeComplete);
        Assert.Single(data.Documents);
        var result = await FdaTrialSummaryRequirementRule.InspectAsync(fixture.Packages, lifecycle, fixture.Context, 20, default);
        Assert.Equal(expected, result!.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, result.Findings.Count);
    }
}
