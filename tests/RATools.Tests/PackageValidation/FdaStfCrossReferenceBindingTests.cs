using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfCrossReferenceBindingTests
{
    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("missing-href", CheckStatus.NotEvaluated)]
    [InlineData("href-conflict", CheckStatus.NotEvaluated)]
    [InlineData("leaf-location", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("default-href", CheckStatus.NotEvaluated)]
    [InlineData("missing-id", CheckStatus.NotEvaluated)]
    [InlineData("id-conflict", CheckStatus.NotEvaluated)]
    [InlineData("missing-operation", CheckStatus.NotEvaluated)]
    [InlineData("operation-conflict", CheckStatus.NotEvaluated)]
    [InlineData("leaf-node-location", CheckStatus.NotEvaluated)]
    [InlineData("derived-location", CheckStatus.NotEvaluated)]
    [InlineData("derived-node-location", CheckStatus.NotEvaluated)]
    [InlineData("alias-href", CheckStatus.Fail)]
    [InlineData("reordered", CheckStatus.Fail)]
    [InlineData("known-before-unknown", CheckStatus.Fail)]
    [InlineData("unknown-before-known", CheckStatus.Fail)]
    public async Task ResolvedLeafDoesNotQualifyConflictingContentEvidence(string scenario, CheckStatus expected)
    {
        var two = scenario is "known-before-unknown" or "unknown-before-known";
        var f = await FdaStfCrossReferenceRuleTests.Fixture(two ? "two-references" : "another-stf");
        var xml = Assert.Single(f.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, b => b.Kind == BackboneKind.Ich);
        var leaf = Assert.Single(index.Leaves, l => l.Id == (scenario == "known-before-unknown" ? "b-alias" : "b"));
        var elements = index.Elements.Select(e => new ParsedXmlElement(e.NodePath, e.ParentPath, e.Name, e.LocalName, e.NamespaceUri,
            e.Attributes.Values.Select(a => e.NodePath != leaf.NodePath ? a : (scenario, a.LocalName) switch
            {
                ("missing-href" or "known-before-unknown" or "unknown-before-known", "href") => a with { ExplicitValue = null },
                ("href-conflict", "href") => a with { ExplicitValue = " other " },
                ("default-href", "href") => a with { IsDefault = true, ExplicitValue = null },
                ("missing-id", "ID") => a with { ExplicitValue = null },
                ("id-conflict", "ID") => a with { ExplicitValue = " other " },
                ("missing-operation", "operation") => a with { ExplicitValue = null },
                ("operation-conflict", "operation") => a with { ExplicitValue = " new " },
                ("alias-href", "href") => a with { Name = "link:href" }, _ => a
            }), e.Children, e.Text, scenario == "leaf-location" && e.NodePath == leaf.NodePath ?
                e.Location with { LogicalPath = "0000/index.xml" } : scenario == "leaf-node-location" && e.NodePath == leaf.NodePath ?
                e.Location with { NodePath = "/wrong" } : e.Location)).ToList();
        if (scenario == "reordered") elements.Reverse();
        var replacement = new ParsedBackbone(index.LogicalPath, scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : index.ProfileSnapshotId,
            index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier,
            elements, index.Nodes, index.Leaves.Select(l => l != leaf ? l : scenario switch
            {
                "derived-location" => l with { Location = l.Location with { LogicalPath = "0000/index.xml" } },
                "derived-node-location" => l with { Location = l.Location with { NodePath = "/wrong" } }, _ => l
            }), index.IdIndex.Values.SelectMany(x => x), index.ResolvedAssets, index.ProcessingInstructions);
        var changed = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Select(b => b == index ? replacement : b), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(f.Packages, f.Lifecycle.ProfileSnapshotId, [changed], f.Lifecycle.Events,
            f.Lifecycle.States, f.Lifecycle.Checks, f.Lifecycle.RulesDigest, f.Lifecycle.AssetManifestDigest);
        var bindings = PackageStfBindingReader.Read(f.Packages, lifecycle, f.Context, default);
        Assert.True(bindings.TargetScopeComplete);
        var source = Assert.Single(bindings.Documents, d => d.Document.LogicalPath.EndsWith("stf-a.xml", StringComparison.Ordinal));
        Assert.Equal(two ? 2 : 1, source.References.Count);
        Assert.All(source.References, r => Assert.Equal(StfReferenceStatus.Resolved, r.Status));
        var check = FdaStfCrossReferenceRule.Inspect(f.Packages, lifecycle, f.Context, 10, default)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected == CheckStatus.Fail ? 1 : 0, check.Findings.Count);
    }
}
