using System.Text.Json;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// Non-owning selection. A companion is explicit; no adjacent ZIP or workspace is discovered.
public sealed class PackageDeliveryInput
{
    public PackageDeliveryInput(PackageInputSet packages, ICapturedPackageInput? comparison = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (comparison is not null && (comparison.Manifest.ApplicationId != packages.Target.Manifest.ApplicationId ||
            comparison.Manifest.SequenceNumber != packages.Target.Manifest.SequenceNumber ||
            comparison.SourceKind == packages.Target.SourceKind || comparison.Limits.Digest() != packages.Target.Limits.Digest()))
            throw new ArgumentException("Compare a directory and a ZIP for the same explicit application, sequence and limits.", nameof(comparison));
        Packages = packages;
        Comparison = comparison;
        InputDigest = comparison is null ? packages.Target.InputDigest : CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, primaryInputDigest = packages.Target.InputDigest, comparisonInputDigest = comparison.InputDigest
        }));
    }

    public PackageInputSet Packages { get; }
    public ICapturedPackageInput? Comparison { get; }
    public string InputDigest { get; }
    public async Task VerifyUnchangedAsync(CancellationToken cancellationToken)
    {
        await Packages.VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
        if (Comparison is not null) await Comparison.VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
    }
}
