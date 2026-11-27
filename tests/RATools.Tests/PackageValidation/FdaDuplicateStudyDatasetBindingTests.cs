using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaDuplicateStudyDatasetBindingTests
{
    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("href-conflict", CheckStatus.NotEvaluated)]
    [InlineData("missing-explicit-href", CheckStatus.NotEvaluated)]
    [InlineData("title-location", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("default-href", CheckStatus.NotEvaluated)]
    [InlineData("href-normalized-conflict", CheckStatus.NotEvaluated)]
    [InlineData("missing-explicit-id", CheckStatus.NotEvaluated)]
    [InlineData("operation-normalized-conflict", CheckStatus.NotEvaluated)]
    [InlineData("leaf-location", CheckStatus.NotEvaluated)]
    [InlineData("leaf-node-location", CheckStatus.NotEvaluated)]
    [InlineData("derived-location", CheckStatus.NotEvaluated)]
    [InlineData("title-node-location", CheckStatus.NotEvaluated)]
    [InlineData("hidden-title-child", CheckStatus.NotEvaluated)]
    [InlineData("hidden-second-title", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-title-pointer", CheckStatus.NotEvaluated)]
    [InlineData("reordered", CheckStatus.Fail)]
    [InlineData("known-before-unknown", CheckStatus.Fail)]
    [InlineData("unknown-before-known", CheckStatus.Fail)]
    public async Task ConflictingLeafEvidenceCannotEstablishADuplicate(string scenario, CheckStatus expected)
    {
        var three = scenario is "known-before-unknown" or "unknown-before-known";
        var f = await FdaDuplicateStudyDatasetRuleTests.Fixture(three ? "three-leaves" : "duplicate");
        var xml = Assert.Single(f.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, b => b.Kind == BackboneKind.Ich);
        var leaf = Assert.Single(index.Leaves, l => l.Id == (scenario == "known-before-unknown" ? "third" : scenario == "unknown-before-known" ? "first" : "second"));
        var title = Assert.Single(index.Elements, e => e.ParentPath == leaf.NodePath && e.LocalName == "title");
        var elements = index.Elements.Select(e => new ParsedXmlElement(e.NodePath, e.ParentPath, e.Name, e.LocalName, e.NamespaceUri,
            e.Attributes.Values.Select(a => e.NodePath != leaf.NodePath ? a : a.LocalName switch
            {
                "href" => scenario switch
                {
                    "href-conflict" => a with { Value = "m4/study/other.xpt", ExplicitValue = "m4/study/other.xpt" },
                    "missing-explicit-href" or "known-before-unknown" or "unknown-before-known" => a with { ExplicitValue = null },
                    "default-href" => a with { IsDefault = true, ExplicitValue = null },
                    "href-normalized-conflict" => a with { ExplicitValue = " other " }, _ => a
                },
                "ID" when scenario == "missing-explicit-id" => a with { ExplicitValue = null },
                "operation" when scenario == "operation-normalized-conflict" => a with { ExplicitValue = " new " },
                _ => a
            }), scenario == "duplicate-title-pointer" && e.NodePath == leaf.NodePath ? e.Children.Concat([title.NodePath]) : e.Children, e.Text,
            e.NodePath == title.NodePath && scenario == "title-location" || e.NodePath == leaf.NodePath && scenario == "leaf-location" ?
                e.Location with { LogicalPath = "0000/index.xml" } :
            e.NodePath == title.NodePath && scenario == "title-node-location" || e.NodePath == leaf.NodePath && scenario == "leaf-node-location" ?
                e.Location with { NodePath = "/wrong" } : e.Location)).ToList();
        if (scenario == "hidden-title-child") elements.Add(new(title.NodePath + "/span", title.NodePath, "span", "span", "", [], [], "nested",
            title.Location with { NodePath = title.NodePath + "/span" }));
        if (scenario == "hidden-second-title") elements.Add(new(title.NodePath + "-other", leaf.NodePath, "title", "title", "", [], [], "Data",
            title.Location with { NodePath = title.NodePath + "-other" }));
        if (scenario == "reordered") elements.Reverse();
        var replacement = new ParsedBackbone(index.LogicalPath, scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : index.ProfileSnapshotId,
            index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier,
            elements, index.Nodes, index.Leaves.Select(l => scenario == "derived-location" && l == leaf ? l with { Location = l.Location with { LogicalPath = "0000/index.xml" } } : l),
            index.IdIndex.Values.SelectMany(x => x), index.ResolvedAssets, index.ProcessingInstructions);
        var changedXml = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Select(b => b == index ? replacement : b), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(f.Packages, f.Lifecycle.ProfileSnapshotId, [changedXml],
            f.Lifecycle.Events, f.Lifecycle.States, f.Lifecycle.Checks, f.Lifecycle.RulesDigest, f.Lifecycle.AssetManifestDigest);
        var data = await PackageStudyDataInspector.InspectAsync(f.Packages, lifecycle, f.Context);
        var document = Assert.Single(data.Documents);
        Assert.Equal(three ? 3 : 2, document.References.Count);
        // Duplicate child pointers are rejected by the shared backbone binder before content reading.
        Assert.All(document.References, r => Assert.Equal(scenario == "duplicate-title-pointer" ?
            StudyDataReferenceStatus.ReferenceUnavailable : StudyDataReferenceStatus.Read, r.Status));
        var check = await FdaDuplicateStudyDatasetRule.InspectAsync(f.Packages, lifecycle, f.Context, 10, default);
        Assert.Equal(expected, check!.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }
}
