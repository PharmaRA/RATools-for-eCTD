using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class DossierProjectionTests
{
    private static readonly string[] AllSequences = ["0000", "0001", "0002", "0003"];

    [Fact]
    public async Task FourSequencesExplainEffectiveLeavesAndTheirHistory()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var (input, lifecycle) = await fixture.SelectAsync("0003");
        var projection = DossierProjectionBuilder.Build(lifecycle, input);
        Assert.True(projection.IsComplete, string.Join(" | ", projection.IncompleteReasons));
        Assert.Equal(AllSequences, projection.AppliedSequences);

        // Alpha is replaced in 0001 and its predecessor is retained as replaced history.
        var alpha = projection.Leaves.Single(leaf => leaf.Address.LeafId == "alpha-spec-v1");
        Assert.Equal(DossierLeafStatus.Replaced, alpha.Status);
        Assert.NotNull(alpha.ReplacedBy);
        // 0002 appends to Alpha v2, so both the target and the appended leaf are
        // effective append members in the cumulative view; bytes are never concatenated.
        var alphaV2 = projection.Leaves.Single(leaf => leaf.Address.LeafId == "alpha-spec-v2");
        Assert.Equal(DossierLeafStatus.AppendMember, alphaV2.Status);
        var addendum = projection.Leaves.Single(leaf => leaf.Address.LeafId == "alpha-addendum");
        Assert.Equal(DossierLeafStatus.AppendMember, addendum.Status);
        Assert.Equal("0001/index.xml#alpha-spec-v2", addendum.AppendedTo);
        // Beta lives in the same section and is not merged with Alpha by section or
        // file name; it stays effective as of 0002 and is deleted by 0003.
        var beta = projection.Leaves.Single(leaf => leaf.Address.LeafId == "beta-spec-v1");
        Assert.Equal(DossierLeafStatus.Deleted, beta.Status);
        Assert.Equal("0003/index.xml#beta-delete", beta.DeletedBy);
        // The historical leaf keeps its own delivered content address; the delete
        // event itself must not invent a new one and is never effective content.
        Assert.Equal("0000/index.xml", beta.ContentLogicalPath);
        var delete = projection.Leaves.Single(leaf => leaf.Address.LeafId == "beta-delete");
        Assert.Equal(DossierLeafStatus.DeleteEvent, delete.Status);
        Assert.Null(delete.ContentLogicalPath);
        Assert.DoesNotContain(projection.EffectiveLeaves, leaf => leaf.Address.LeafId == "beta-delete");
        await using var earlier = new PackageFileInspectorTests.Fixture();
        var (asOfTwo, lifecycleTwo) = await earlier.SelectAsync("0002");
        var projectionAsOfTwo = DossierProjectionBuilder.Build(lifecycleTwo, asOfTwo);
        Assert.Contains(projectionAsOfTwo.EffectiveLeaves, leaf => leaf.Address.LeafId == "beta-spec-v1");
        Assert.DoesNotContain(projection.Leaves, leaf => leaf.Address.LeafId == "beta-delete" && leaf.ContentLogicalPath is not null);

        // Every effective leaf carries a real address; none is inferred from a name.
        Assert.All(projection.EffectiveLeaves, leaf => Assert.NotEmpty(leaf.Address.LeafId));
        Assert.Equal(projection.Leaves.Count, projection.Leaves.Select(leaf => leaf.Address).Distinct().Count());
    }

    [Fact]
    public async Task MissingHistoryMakesTheProjectionIncompleteInsteadOfEffective()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        // Select 0002 while omitting 0001 from the explicit baseline.
        var (input, lifecycle) = await fixture.SelectAsync("0002", history: ["0000"]);
        var projection = DossierProjectionBuilder.Build(lifecycle, input);
        Assert.False(projection.IsComplete);
        Assert.NotEmpty(projection.IncompleteReasons);
        Assert.DoesNotContain(projection.EffectiveLeaves, leaf => leaf.Address.LeafId == "alpha-spec-v2");
        // The unresolved event is preserved for review rather than dropped.
        Assert.Contains(projection.Events, item => item.Status != CheckStatus.Pass && item.UnresolvedReason is not null);
    }

    [Fact]
    public async Task ProjectionBindsTheExactInputsAndBaseline()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var (input, lifecycle) = await fixture.SelectAsync("0001");
        var projection = DossierProjectionBuilder.Build(lifecycle, input);
        Assert.Equal(input.InputDigest, projection.InputDigest);
        Assert.Equal(input.Packages.Baseline.Digest, projection.BaselineDigest);
        Assert.Equal(input.Packages.Target.Limits.Digest(), projection.LimitsDigest);
        Assert.Equal("0001", projection.TargetSequence);
        Assert.All(projection.Baseline, entry => Assert.False(string.IsNullOrWhiteSpace(entry.SourceKind)));
        // Rebuilding from the same facts is deterministic: same leaves, statuses and
        // event order, not merely the same digest.
        var rebuilt = DossierProjectionBuilder.Build(lifecycle, input);
        Assert.Equal(projection.Leaves, rebuilt.Leaves);
        Assert.Equal(projection.Events, rebuilt.Events);
        Assert.Equal(projection.IsComplete, rebuilt.IsComplete);
        // Exact input binding: changing one captured byte changes the projection identity.
        File.AppendAllText(fixture.PathOf("0001/index.xml"), "\n");
        var (changedInput, changedLifecycle) = await fixture.SelectAsync("0001");
        Assert.NotEqual(projection.InputDigest,
            DossierProjectionBuilder.Build(changedLifecycle, changedInput).InputDigest);
    }

    [Fact]
    public async Task ComparisonInputBindsTheCombinedDeliveryDigest()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        fixture.CreateZip("0000", "same");
        var (input, lifecycle) = await fixture.SelectAsync("0000", comparison: true);
        var projection = DossierProjectionBuilder.Build(lifecycle, input);
        // The archived projection must bind the report's exact input manifest digest,
        // which includes the explicit comparison input.
        Assert.Equal(input.InputDigest, projection.InputDigest);
        Assert.NotEqual(input.Packages.Target.InputDigest, projection.InputDigest);
        Assert.True(projection.IsComplete, string.Join(" | ", projection.IncompleteReasons));
    }

    [Fact]
    public async Task ChangedInTargetReportsOnlyAdditionsAndInvalidations()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var (input, lifecycle) = await fixture.SelectAsync("0001");
        var projection = DossierProjectionBuilder.Build(lifecycle, input);
        Assert.Contains(projection.ChangedInTarget, leaf => leaf.Address.LeafId == "alpha-spec-v2");
        Assert.Contains(projection.ChangedInTarget, leaf => leaf.Address.LeafId == "alpha-spec-v1");
    }
}
