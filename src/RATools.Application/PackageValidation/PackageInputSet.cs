using System.Collections.Frozen;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// Non-owning logical mapping. The caller owns and disposes each captured input.
public sealed class PackageInputSet
{
    private readonly FrozenDictionary<string, ICapturedPackageInput> _sequences;

    public PackageInputSet(ICapturedPackageInput target, HistoryBaselineManifest baseline,
        IEnumerable<ICapturedPackageInput> history)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(baseline);
        if (target.Manifest.ApplicationId != baseline.ApplicationId || target.Manifest.SequenceNumber != baseline.TargetSequence)
            throw new ArgumentException("The baseline must belong to this exact application and target sequence.", nameof(baseline));
        var inputs = history.ToDictionary(input => input.Manifest.SequenceNumber, StringComparer.Ordinal);
        if (inputs.Count != baseline.Entries.Count) throw new ArgumentException("Every baseline entry requires exactly one selected captured source.", nameof(history));
        foreach (var entry in baseline.Entries)
        {
            if (!inputs.TryGetValue(entry.SequenceNumber, out var input) || input.Manifest.ApplicationId != baseline.ApplicationId ||
                input.SourceId != entry.SourceId || input.InputDigest != entry.ContentDigest || input.Limits.Digest() != target.Limits.Digest())
                throw new ArgumentException("Historical source identity or content digest differs from the explicit baseline.", nameof(history));
        }
        inputs.Add(target.Manifest.SequenceNumber, target);
        _sequences = inputs.ToFrozenDictionary(StringComparer.Ordinal);
        Target = target;
        Baseline = baseline;
    }

    public ICapturedPackageInput Target { get; }
    public HistoryBaselineManifest Baseline { get; }
    public IReadOnlyDictionary<string, ICapturedPackageInput> Sequences => _sequences;

    public Stream OpenRead(string logicalPath)
    {
        PackageLogicalPath.Validate(logicalPath);
        var number = logicalPath.Split('/')[0];
        if (!_sequences.TryGetValue(number, out var input))
            throw new PackageInputException("HISTORY-BASELINE", "HISTORY_NOT_AVAILABLE", "The logical sequence is not in the explicitly selected input set.", logicalPath);
        return input.OpenRead(logicalPath);
    }

    public async Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var input in _sequences.Values)
            await input.VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
    }
}
