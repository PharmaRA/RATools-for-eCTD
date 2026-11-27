using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Application.PackageValidation;

internal enum CapturedDefineStatus { Read, Missing, OutsideSelection, Unavailable, InputChanged, LimitExceeded }
internal sealed record CapturedDefineFacts(CapturedDefineStatus Status, string SourceId, string InputDigest,
    string LogicalPath, string SelectionDigest, PackageDefineXmlFacts? Content);

internal static class PackageCapturedDefineXmlReader
{
    internal static async Task<CapturedDefineFacts> ReadAsync(ICapturedPackageInput capture, string logicalPath,
        DefineXmlReadLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= new();
        if (limits.MaxBytes < 0 || limits.MaxNodes < 0 || limits.MaxDepth < 0 || limits.MaxCharacters < 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
        var sourceId = capture.SourceId;
        var inputDigest = capture.InputDigest;
        var manifest = capture.Manifest;
        var captureLimits = capture.Limits;
        captureLimits.Validate();
        var captureLimitsDigest = captureLimits.Digest();
        var selectedLimits = limits with
        {
            MaxBytes = (int)Math.Min(limits.MaxBytes, captureLimits.MaxFileBytes),
            MaxNodes = Math.Min(limits.MaxNodes, captureLimits.MaxXmlNodes),
            MaxDepth = Math.Min(limits.MaxDepth, captureLimits.MaxXmlDepth),
            MaxCharacters = Math.Min(limits.MaxCharacters, captureLimits.MaxXmlCharacters)
        };
        var selectionDigest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, readerVersion = "captured-define-v1;define-declarations-v1", sourceId, inputDigest,
            manifestDigest = manifest.Digest, logicalPath, limits, selectedLimits, captureLimitsDigest
        }));
        try { PackageLogicalPath.Validate(logicalPath, captureLimits.MaxPathLength, captureLimits.MaxPathDepth); }
        catch (ArgumentException) { return Result(CapturedDefineStatus.OutsideSelection); }
        if (!logicalPath.StartsWith(manifest.SequenceNumber + "/", StringComparison.Ordinal)) return Result(CapturedDefineStatus.OutsideSelection);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(captureLimits.MaxElapsedSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var token = linked.Token;
        try
        {
            await capture.VerifyUnchangedAsync(token).ConfigureAwait(false);
            if (!SameIdentity()) return Result(CapturedDefineStatus.InputChanged);
            var matches = manifest.Files.Where(file => file.LogicalPath == logicalPath).Take(2).ToArray();
            if (matches.Length == 0) return Result(CapturedDefineStatus.Missing);
            if (matches.Length != 1 || matches[0].Length < 0) return Result(CapturedDefineStatus.Unavailable);
            var file = matches[0];
            if (file.Length > selectedLimits.MaxBytes) return Result(CapturedDefineStatus.LimitExceeded);
            token.ThrowIfCancellationRequested();
            var bytes = new byte[(int)file.Length];
            using (var stream = capture.OpenRead(logicalPath))
            {
                await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                var extra = new byte[1];
                if (await stream.ReadAsync(extra, token).ConfigureAwait(false) != 0) return Result(CapturedDefineStatus.InputChanged);
            }
            var content = PackageDefineXmlReader.Read(bytes, file.Sha256, selectedLimits, token);
            if (content.Status == DefineXmlReadStatus.InputMismatch) return Result(CapturedDefineStatus.InputChanged);
            await capture.VerifyUnchangedAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!SameIdentity()) return Result(CapturedDefineStatus.InputChanged);
            return Result(CapturedDefineStatus.Read, content);
        }
        catch (PackageInputChangedException) { return Result(CapturedDefineStatus.InputChanged); }
        catch (EndOfStreamException) { return Result(CapturedDefineStatus.InputChanged); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { return Result(CapturedDefineStatus.Unavailable); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { return Result(CapturedDefineStatus.Unavailable); }

        bool SameIdentity() => capture.SourceId == sourceId && capture.InputDigest == inputDigest &&
            capture.Manifest.Digest == manifest.Digest && capture.Limits.Digest() == captureLimitsDigest;
        CapturedDefineFacts Result(CapturedDefineStatus status, PackageDefineXmlFacts? content = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(status, sourceId, inputDigest, logicalPath, selectionDigest, content);
        }
    }
}
