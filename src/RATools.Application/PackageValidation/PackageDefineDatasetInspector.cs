using System.Text.Json;
using RATools.Domain.Common;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class PackageDefineDatasetInspector
{
    internal static async Task<PackageDefineDatasetInspection> InspectAsync(ICapturedPackageInput capture, string definePath,
        DefineXmlReadLimits? defineLimits = null, XptReadLimits? xptLimits = null,
        XptTextEncoding encoding = XptTextEncoding.AsciiOnly, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();
        defineLimits ??= new(); xptLimits ??= new();
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        if (defineLimits.MaxBytes < 0 || defineLimits.MaxNodes < 0 || defineLimits.MaxDepth < 0 || defineLimits.MaxCharacters < 0)
            throw new ArgumentOutOfRangeException(nameof(defineLimits));
        if (xptLimits.MaxBytes < 0 || xptLimits.MaxVariables < 0 || xptLimits.MaxRecordBytes < 0 || xptLimits.MaxRows < 0 || xptLimits.MaxCells < 0)
            throw new ArgumentOutOfRangeException(nameof(xptLimits));
        var source = capture.SourceId; var digest = capture.InputDigest; var manifest = capture.Manifest; var limits = capture.Limits;
        limits.Validate();
        var limitsDigest = limits.Digest();
        var selection = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, readerVersion = "define-datasets-v1;captured-define-v1;define-declarations-v1;captured-xpt-v1;xpt-v5-v1",
            source, digest, manifestDigest = manifest.Digest, limitsDigest, definePath, defineLimits, xptLimits, encoding = encoding.ToString()
        }));
        var work = limits.MaxXmlNodes;
        var remainingBytes = xptLimits.MaxBytes;
        var remainingRows = xptLimits.MaxRows;
        var remainingCells = xptLimits.MaxCells;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(limits.MaxElapsedSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var token = linked.Token;
        try
        {
            await capture.VerifyUnchangedAsync(token).ConfigureAwait(false);
            if (!SameIdentity()) return Result(DefineDatasetInspectionStatus.InputChanged);
            var define = await PackageCapturedDefineXmlReader.ReadAsync(capture, definePath, defineLimits, token).ConfigureAwait(false);
            if (!SameIdentity() || define.Status == CapturedDefineStatus.InputChanged || define.SourceId != source || define.InputDigest != digest)
                return Result(DefineDatasetInspectionStatus.InputChanged);
            if (define is not { Status: CapturedDefineStatus.Read, Content: { Status: DefineXmlReadStatus.Read, ReadComplete: true } })
                return Result(DefineDatasetInspectionStatus.DefineUnavailable, define);
            var files = new Dictionary<string, List<PackageInputFile>>(StringComparer.Ordinal);
            foreach (var file in manifest.Files)
            {
                Tick();
                if (!files.TryGetValue(file.LogicalPath, out var entries)) files.Add(file.LogicalPath, entries = []);
                entries.Add(file);
            }
            var results = new List<DefineDatasetContent>();
            var cache = new Dictionary<string, (DefineDatasetContentStatus Status, CapturedXptFacts? Xpt)>(StringComparer.Ordinal);
            foreach (var declaration in define.Content.Datasets)
            {
                Tick();
                if (declaration.FileReference != DefineReferenceStatus.Resolved || declaration.File?.Href is not { Length: > 0 } href)
                { Add(DefineDatasetContentStatus.ReferenceUnavailable); continue; }
                if (href.Length > limits.MaxPathLength || href.Any(character => character is < '!' or > '~') ||
                    href.IndexOfAny(['%', '?', '#', ':', '\\']) >= 0 || href.StartsWith('/'))
                { Add(DefineDatasetContentStatus.UnsupportedPath); continue; }
                string path;
                try
                {
                    var reference = PackageLogicalPath.ResolveReference(definePath, href);
                    if (reference.LogicalPath is not { } resolved || reference.Query is not null || reference.Fragment is not null ||
                        !resolved.StartsWith(manifest.SequenceNumber + "/", StringComparison.Ordinal))
                    { Add(DefineDatasetContentStatus.UnsupportedPath); continue; }
                    path = PackageLogicalPath.Validate(resolved, limits.MaxPathLength, limits.MaxPathDepth);
                }
                catch (ArgumentException) { Add(DefineDatasetContentStatus.UnsupportedPath); continue; }
                if (cache.TryGetValue(path, out var cached)) { Add(cached.Status, path, cached.Xpt); continue; }
                if (!files.TryGetValue(path, out var matches)) { Add(DefineDatasetContentStatus.Missing, path); continue; }
                if (matches.Count != 1 || matches[0].Length < 0) { Add(DefineDatasetContentStatus.Unavailable, path); continue; }
                if (matches[0].Length > remainingBytes) { Add(DefineDatasetContentStatus.LimitExceeded, path); continue; }
                remainingBytes -= (int)matches[0].Length;
                var xpt = await PackageCapturedXptReader.ReadAsync(capture, path, encoding,
                    xptLimits with { MaxRows = remainingRows, MaxCells = remainingCells }, token).ConfigureAwait(false);
                if (!SameIdentity() || xpt.Status == CapturedXptStatus.InputChanged || xpt.SourceId != source || xpt.InputDigest != digest)
                    return Result(DefineDatasetInspectionStatus.InputChanged);
                var status = xpt.Status switch
                {
                    CapturedXptStatus.Read => DefineDatasetContentStatus.Read,
                    CapturedXptStatus.Missing => DefineDatasetContentStatus.Missing,
                    CapturedXptStatus.LimitExceeded => DefineDatasetContentStatus.LimitExceeded,
                    _ => DefineDatasetContentStatus.Unavailable
                };
                if (xpt.Content is { } content)
                {
                    remainingRows -= content.Rows.Count;
                    foreach (var row in content.Rows) { Tick(); remainingCells -= row.Cells.Count; }
                }
                cache.Add(path, (status, status == DefineDatasetContentStatus.Read ? xpt : null));
                Add(status, path, status == DefineDatasetContentStatus.Read ? xpt : null);

                void Add(DefineDatasetContentStatus status, string? logicalPath = null, CapturedXptFacts? facts = null) =>
                    results.Add(new(declaration, status, logicalPath, facts));
            }
            await capture.VerifyUnchangedAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!SameIdentity()) return Result(DefineDatasetInspectionStatus.InputChanged);
            return Result(DefineDatasetInspectionStatus.Read, define, true, results);
        }
        catch (PackageInputChangedException) { return Result(DefineDatasetInspectionStatus.InputChanged); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { return Result(DefineDatasetInspectionStatus.Unavailable); }
        catch (AssociationLimitException) { return Result(DefineDatasetInspectionStatus.LimitExceeded); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { return Result(DefineDatasetInspectionStatus.Unavailable); }

        void Tick() { token.ThrowIfCancellationRequested(); if (work-- <= 0) throw new AssociationLimitException(); }
        bool SameIdentity() => capture.SourceId == source && capture.InputDigest == digest &&
            capture.Manifest.Digest == manifest.Digest && capture.Limits.Digest() == limitsDigest;
        PackageDefineDatasetInspection Result(DefineDatasetInspectionStatus status, CapturedDefineFacts? define = null,
            bool complete = false, IEnumerable<DefineDatasetContent>? datasets = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(status, selection, define, complete, datasets);
        }
    }
    private sealed class AssociationLimitException : Exception;
}
