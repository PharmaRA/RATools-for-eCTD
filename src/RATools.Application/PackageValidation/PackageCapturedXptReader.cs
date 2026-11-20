using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Application.PackageValidation;

internal enum CapturedXptStatus { Read, Missing, OutsideSelection, Unavailable, InputChanged, LimitExceeded }
internal sealed record CapturedXptFacts(CapturedXptStatus Status, string SourceId, string InputDigest, string LogicalPath,
    string SelectionDigest, PackageXptFacts? Content);

internal static class PackageCapturedXptReader
{
    internal static async Task<CapturedXptFacts> ReadAsync(ICapturedPackageInput capture, string logicalPath,
        XptTextEncoding encoding = XptTextEncoding.AsciiOnly, XptReadLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= new();
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        if (limits.MaxBytes < 0 || limits.MaxVariables < 0 || limits.MaxRecordBytes < 0 || limits.MaxRows < 0 || limits.MaxCells < 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
        var sourceId = capture.SourceId;
        var inputDigest = capture.InputDigest;
        var manifest = capture.Manifest;
        var captureLimits = capture.Limits;
        var captureLimitsDigest = captureLimits.Digest();
        var selectionDigest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, readerVersion = "captured-xpt-v1;xpt-v5-v1", sourceId, inputDigest,
            manifestDigest = manifest.Digest, logicalPath, encoding = encoding.ToString(), limits, captureLimitsDigest
        }));
        try { PackageLogicalPath.Validate(logicalPath, captureLimits.MaxPathLength, captureLimits.MaxPathDepth); }
        catch (ArgumentException) { return Result(CapturedXptStatus.OutsideSelection); }
        if (!logicalPath.StartsWith(manifest.SequenceNumber + "/", StringComparison.Ordinal)) return Result(CapturedXptStatus.OutsideSelection);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(captureLimits.MaxElapsedSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var token = linked.Token;
        try
        {
            await capture.VerifyUnchangedAsync(token).ConfigureAwait(false);
            if (!SameIdentity()) return Result(CapturedXptStatus.InputChanged);
            var matches = manifest.Files.Where(file => file.LogicalPath == logicalPath).Take(2).ToArray();
            if (matches.Length == 0) return Result(CapturedXptStatus.Missing);
            if (matches.Length != 1) return Result(CapturedXptStatus.Unavailable);
            var file = matches[0];
            if (file.Length < 0) return Result(CapturedXptStatus.Unavailable);
            if (file.Length > limits.MaxBytes || file.Length > captureLimits.MaxFileBytes || file.Length > int.MaxValue)
                return Result(CapturedXptStatus.LimitExceeded);
            token.ThrowIfCancellationRequested();
            var bytes = new byte[(int)file.Length];
            using (var stream = capture.OpenRead(logicalPath))
            {
                await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                var extra = new byte[1];
                if (await stream.ReadAsync(extra, token).ConfigureAwait(false) != 0) return Result(CapturedXptStatus.InputChanged);
            }
            var content = PackageXptReader.Read(bytes, file.Sha256, encoding, limits, token);
            if (content.Status == XptReadStatus.InputMismatch) return Result(CapturedXptStatus.InputChanged);
            await capture.VerifyUnchangedAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!SameIdentity()) return Result(CapturedXptStatus.InputChanged);
            return Result(CapturedXptStatus.Read, content);
        }
        catch (PackageInputChangedException) { return Result(CapturedXptStatus.InputChanged); }
        catch (EndOfStreamException) { return Result(CapturedXptStatus.InputChanged); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { return Result(CapturedXptStatus.Unavailable); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { return Result(CapturedXptStatus.Unavailable); }

        bool SameIdentity() => capture.SourceId == sourceId && capture.InputDigest == inputDigest &&
            capture.Manifest.Digest == manifest.Digest && capture.Limits.Digest() == captureLimitsDigest;
        CapturedXptFacts Result(CapturedXptStatus status, PackageXptFacts? content = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(status, sourceId, inputDigest, logicalPath, selectionDigest, content);
        }
    }
}
