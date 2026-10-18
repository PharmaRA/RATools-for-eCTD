using RATools.Application.PackageValidation;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

// The cumulative view and the lifecycle checks must reach the same conclusion from the
// same verified facts. This is the P3-06 alignment boundary: if the two disagree, one of
// them is presenting an operational state the other does not support.
public sealed class LifecycleProjectionAlignmentTests
{
    private static readonly string[] Sequences = ["0000", "0001", "0002", "0003"];

    [Theory]
    [InlineData("0000")]
    [InlineData("0001")]
    [InlineData("0002")]
    [InlineData("0003")]
    public async Task EffectiveLeavesAndInvalidationStatesMatchLifecycleChecks(string sequence)
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var (input, lifecycle) = await fixture.SelectAsync(sequence);
        var projection = DossierProjectionBuilder.Build(lifecycle, input);
        Assert.True(projection.IsComplete, string.Join(" | ", projection.IncompleteReasons));

        // The lifecycle facts and the projection are built from the same capture set.
        Assert.Equal(lifecycle.InputDigest, projection.InputDigest);
        Assert.Equal(lifecycle.HistoryManifestDigest, projection.BaselineDigest);
        Assert.Equal(lifecycle.ProfileSnapshotId, projection.ProfileSnapshotId);

        var projectedEffective = projection.Leaves
            .Where(leaf => leaf.Status is DossierLeafStatus.Current or DossierLeafStatus.AppendMember)
            .Select(leaf => leaf.Address).OrderBy(Key, StringComparer.Ordinal).ToArray();
        var checkedEffective = lifecycle.States
            .Where(state => state.Effectiveness == PackageLeafEffectiveness.Current)
            .Select(state => state.Address).OrderBy(Key, StringComparer.Ordinal).ToArray();
        Assert.Equal(checkedEffective, projectedEffective);

        // Invalidation is the same fact in both views: replaced, deleted and unresolved
        // leaves must not be reported as effective by either.
        foreach (var state in lifecycle.States.Where(state => state.Effectiveness != PackageLeafEffectiveness.Current))
        {
            var leaf = projection.Leaves.Single(item => item.Address == state.Address);
            Assert.Equal(state.Effectiveness switch
            {
                PackageLeafEffectiveness.Replaced => DossierLeafStatus.Replaced,
                PackageLeafEffectiveness.Deleted => DossierLeafStatus.Deleted,
                PackageLeafEffectiveness.DeleteEvent => DossierLeafStatus.DeleteEvent,
                PackageLeafEffectiveness.Unknown => DossierLeafStatus.Unknown,
                _ => throw new InvalidOperationException("Unexpected lifecycle effectiveness.")
            }, leaf.Status);
        }

        // A delete event is a lifecycle instruction, never a delivered leaf.
        Assert.DoesNotContain(projection.EffectiveLeaves, leaf => leaf.Operation == "delete");
        Assert.All(projection.Leaves.Where(leaf => leaf.Operation == "delete"), leaf => Assert.Null(leaf.ContentLogicalPath));
    }

    private static string Key(LeafAddress address) =>
        $"{address.SequenceNumber}/{address.BackboneRelativePath}#{address.LeafId}";
}
