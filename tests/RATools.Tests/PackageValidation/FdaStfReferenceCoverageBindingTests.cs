using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfReferenceCoverageBindingTests
{
    [Theory]
    [InlineData("valid", CheckStatus.Fail)]
    [InlineData("missing-href", CheckStatus.NotEvaluated)]
    [InlineData("operation-conflict", CheckStatus.NotEvaluated)]
    [InlineData("leaf-location", CheckStatus.NotEvaluated)]
    [InlineData("wrong-profile", CheckStatus.NotEvaluated)]
    [InlineData("missing-id", CheckStatus.NotEvaluated)]
    [InlineData("id-conflict", CheckStatus.NotEvaluated)]
    [InlineData("default-href", CheckStatus.NotEvaluated)]
    [InlineData("href-conflict", CheckStatus.NotEvaluated)]
    [InlineData("leaf-node-location", CheckStatus.NotEvaluated)]
    [InlineData("derived-location", CheckStatus.NotEvaluated)]
    [InlineData("derived-node-location", CheckStatus.NotEvaluated)]
    [InlineData("wrong-application", CheckStatus.NotEvaluated)]
    [InlineData("wrong-sequence", CheckStatus.NotEvaluated)]
    [InlineData("wrong-backbone", CheckStatus.NotEvaluated)]
    [InlineData("wrong-leaf-id", CheckStatus.NotEvaluated)]
    [InlineData("reordered", CheckStatus.Fail)]
    [InlineData("unknown-first", CheckStatus.Fail)]
    [InlineData("unknown-second", CheckStatus.Fail)]
    public async Task CompleteDiscoveryCannotQualifyConflictingLeafFacts(string scenario, CheckStatus expected)
    {
        var f = await FdaStfReferenceCoverageRuleTests.Fixture("no-stf");
        var xml = Assert.Single(f.Lifecycle.XmlInspections);
        var index = Assert.Single(xml.Backbones, b => b.Kind == BackboneKind.Ich);
        var selected = index.Leaves.Where(l => l.Id is "content-a" or "content-b")
            .Where(l => scenario != "unknown-first" || l.Id == "content-a").Where(l => scenario != "unknown-second" || l.Id == "content-b")
            .Select(l => l.NodePath).ToHashSet(StringComparer.Ordinal);
        var elements = index.Elements.Select(e => new ParsedXmlElement(e.NodePath, e.ParentPath, e.Name, e.LocalName, e.NamespaceUri,
            e.Attributes.Values.Select(a => !selected.Contains(e.NodePath) ? a : (scenario, a.LocalName) switch
            {
                ("missing-href" or "unknown-first" or "unknown-second", "href") => a with { ExplicitValue = null },
                ("operation-conflict", "operation") => a with { ExplicitValue = " new " },
                ("missing-id", "ID") => a with { ExplicitValue = null },
                ("id-conflict", "ID") => a with { ExplicitValue = " wrong " },
                ("default-href", "href") => a with { IsDefault = true, ExplicitValue = null },
                ("href-conflict", "href") => a with { ExplicitValue = " other " }, _ => a
            }), e.Children, e.Text, scenario == "leaf-location" && selected.Contains(e.NodePath) ?
                e.Location with { LogicalPath = "0000/index.xml" } : scenario == "leaf-node-location" && selected.Contains(e.NodePath) ?
                e.Location with { NodePath = "/wrong" } : e.Location)).ToList();
        if (scenario == "reordered") elements.Reverse();
        var leaves = index.Leaves.Select(l => !selected.Contains(l.NodePath) ? l : scenario switch
        {
            "derived-location" => l with { Location = l.Location with { LogicalPath = "0000/index.xml" } },
            "derived-node-location" => l with { Location = l.Location with { NodePath = "/wrong" } },
            "wrong-application" => l with { Address = new(Guid.NewGuid(), "0001", "index.xml", l.Id!) },
            "wrong-sequence" => l with { Address = new(f.Packages.Target.Manifest.ApplicationId, "0000", "index.xml", l.Id!) },
            "wrong-backbone" => l with { Address = new(f.Packages.Target.Manifest.ApplicationId, "0001", "other.xml", l.Id!) },
            "wrong-leaf-id" => l with { Address = new(f.Packages.Target.Manifest.ApplicationId, "0001", "index.xml", "different") },
            _ => l
        });
        var replacement = new ParsedBackbone(index.LogicalPath, scenario == "wrong-profile" ? PackageValidationCatalog.EuProfile : index.ProfileSnapshotId,
            index.Kind, index.ReadComplete, index.DtdStatus, index.DocumentTypeName, index.SystemIdentifier, index.PublicIdentifier,
            elements, index.Nodes, leaves, index.IdIndex.Values.SelectMany(x => x), index.ResolvedAssets, index.ProcessingInstructions);
        var changed = new PackageXmlInspection(xml.InputDigest, xml.ProfileSnapshotId, xml.AssetManifestDigest, xml.LimitsDigest,
            xml.Backbones.Select(b => b == index ? replacement : b), xml.Checks, xml.RulesDigest, xml.StudyTaggingFiles);
        var lifecycle = new PackageLifecycleInspection(f.Packages, f.Lifecycle.ProfileSnapshotId, [changed], f.Lifecycle.Events,
            f.Lifecycle.States, f.Lifecycle.Checks, f.Lifecycle.RulesDigest, f.Lifecycle.AssetManifestDigest);
        Assert.True(PackageStfBindingReader.Read(f.Packages, lifecycle, f.Context, default).TargetScopeComplete);
        var check = FdaStfReferenceCoverageRule.Inspect(f.Packages, lifecycle, f.Context, 10, default)!;
        Assert.Equal(expected, check.Status);
        Assert.Equal(scenario is "unknown-first" or "unknown-second" ? 1 : expected == CheckStatus.Fail ? 2 : 0, check.Findings.Count);
        if (scenario is "unknown-first" or "unknown-second")
            Assert.Equal(scenario == "unknown-first" ? "content-b" : "content-a", Assert.Single(check.Findings).Location.LeafId);
    }
}
