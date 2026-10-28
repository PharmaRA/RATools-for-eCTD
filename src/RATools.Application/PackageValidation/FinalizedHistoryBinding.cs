using RATools.Application.Finalization;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// P3-07: the only history that may claim verified trust is a finalized snapshot of
// exactly those bytes.
//
// The external-input API cannot assert trust - the caller only names an input id - so a
// verified entry is derived here from persisted evidence: a finalized snapshot whose
// recorded validated input digest is the selected input's digest. The snapshot also keeps
// its validation run, so the trust claim is auditable back to the report that authorized
// it. Anything else stays an unverified external selection, and the archived baseline is
// re-checked on every inspection so a later edit cannot retroactively verify old history.
public sealed class FinalizedHistoryResolver(IFinalizationStore store)
{
    public async Task<FinalizedHistoryMatch?> ResolveAsync(Guid applicationId, string sequenceNumber,
        string contentDigest, CancellationToken cancellationToken = default)
    {
        var snapshots = await store.ListBySequenceAsync(applicationId, sequenceNumber, cancellationToken)
            .ConfigureAwait(false);
        var matches = snapshots
            .Where(snapshot => string.Equals(snapshot.ValidationInputManifestDigest, contentDigest, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0) return null;
        // Identical bytes may be finalized once per validation mode; a formal snapshot is
        // the stronger record of the same content, so it names the delivered version.
        var best = matches.OrderByDescending(snapshot => snapshot.ValidationMode == PackageValidationMode.Formal)
            .ThenByDescending(snapshot => snapshot.CreatedUtc).ThenByDescending(snapshot => snapshot.Id).First();
        return new FinalizedHistoryMatch(best.Id, best.ValidationRunId);
    }

    // Re-proves an archived finalized entry before its trust is used: the snapshot must
    // still exist, still bind this content, and still be the run the entry recorded.
    public async Task<bool> VerifyAsync(HistoryBaselineEntry entry, Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.SourceKind != HistorySourceKind.Finalized) return entry.TrustStatus != HistoryTrustStatus.VerifiedForScope;
        if (entry.FinalizedSequenceId is not { } id || entry.ValidationRunId is not { } runId ||
            entry.TrustStatus != HistoryTrustStatus.VerifiedForScope)
            return false;
        var snapshot = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return snapshot is not null &&
            snapshot.ApplicationId == applicationId &&
            string.Equals(snapshot.SequenceNumber, entry.SequenceNumber, StringComparison.Ordinal) &&
            string.Equals(snapshot.ValidationInputManifestDigest, entry.ContentDigest, StringComparison.Ordinal) &&
            snapshot.ValidationRunId == runId;
    }
}

public sealed record FinalizedHistoryMatch(Guid FinalizedSequenceId, Guid ValidationRunId);
