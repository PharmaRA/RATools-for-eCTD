using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Application.PackageValidation;

internal static class PackageStudyDataInspector
{
    internal static async Task<PackageStudyDataInspection> InspectAsync(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, XptTextEncoding encoding = XptTextEncoding.AsciiOnly,
        XptReadLimits? limits = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= new();
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        if (limits.MaxBytes < 0 || limits.MaxVariables < 0 || limits.MaxRecordBytes < 0 || limits.MaxRows < 0 || limits.MaxCells < 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
        var target = packages.Target;
        var inputDigest = target.InputDigest;
        var selectionDigest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, readerVersion = "study-data-binding-v1;captured-xpt-v1;xpt-v5-v1;ts-v1",
            inputDigest, baseline = packages.Baseline.Digest, lifecycle.ProfileSnapshotId,
            rules = context.Catalog.Digest, assets = context.Assets.Digest, captureLimits = target.Limits.Digest(),
            encoding = encoding.ToString(), limits
        }));
        var documents = new List<StudyDataDocument>();
        var complete = true;
        var remainingBytes = limits.MaxBytes;
        var remainingRows = limits.MaxRows;
        var remainingCells = limits.MaxCells;
        var work = target.Limits.MaxXmlNodes;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(target.Limits.MaxElapsedSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;
        try
        {
            await packages.VerifyUnchangedAsync(token).ConfigureAwait(false);
            var graph = PackageStfBindingReader.Read(packages, lifecycle, context, token);
            complete = graph.TargetScopeComplete;
            var cache = new Dictionary<string, (StudyDataReferenceStatus Status, CapturedXptFacts? Xpt, PackageTrialSummaryFacts? Ts)>(StringComparer.Ordinal);
            foreach (var document in graph.Documents)
            {
                Tick();
                var owners = document.Owners.Where(owner => owner.Leaf.Address?.SequenceNumber == target.Manifest.SequenceNumber).ToArray();
                if (owners.Length == 0) continue;
                var references = new List<StudyDataReference>();
                documents.Add(new(document, references));
                var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
                var unique = true;
                foreach (var element in document.Document.Elements) { Tick(); unique &= elements.TryAdd(element.NodePath, element); }
                var qualified = document.StructureComplete && unique && owners.All(owner => owner.Qualified);
                if (!qualified) complete = false;
                foreach (var reference in document.References)
                {
                    Tick();
                    if (!qualified ||
                        reference.Status != StfReferenceStatus.Resolved || reference.Leaf?.Address is not { } address)
                    { Add(StudyDataReferenceStatus.ReferenceUnavailable); continue; }
                    PackageReference resolved;
                    try
                    {
                        resolved = PackageLogicalPath.ResolveReference(address.SequenceNumber + "/" + address.BackboneRelativePath, reference.Leaf.Href ?? "");
                    }
                    catch (ArgumentException) { Add(StudyDataReferenceStatus.InvalidContentReference); continue; }
                    if (resolved.LogicalPath is not { } path || resolved.Query is not null || resolved.Fragment is not null ||
                        string.CompareOrdinal(path.Split('/')[0], address.SequenceNumber) > 0)
                    { Add(StudyDataReferenceStatus.InvalidContentReference); continue; }
                    if (!path.EndsWith(".xpt", StringComparison.OrdinalIgnoreCase) && !HasDatasetTag(reference.Element, elements))
                    { Add(StudyDataReferenceStatus.NotSelected, path); continue; }
                    if (cache.TryGetValue(path, out var existing))
                    { Add(existing.Status, path, existing.Xpt, existing.Ts); continue; }
                    var number = path.Split('/')[0];
                    if (!packages.Sequences.TryGetValue(number, out var capture)) { Add(StudyDataReferenceStatus.HistoryUnavailable, path); continue; }
                    var matches = new List<RATools.Domain.PackageValidation.PackageInputFile>();
                    foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == path) matches.Add(file); }
                    if (matches.Count == 0) { Add(StudyDataReferenceStatus.MissingContent, path); continue; }
                    if (matches.Count != 1 || matches[0].Length < 0) { Add(StudyDataReferenceStatus.Unavailable, path); continue; }
                    var length = matches[0].Length;
                    if (length > remainingBytes) { Add(StudyDataReferenceStatus.LimitExceeded, path); continue; }
                    remainingBytes -= (int)length;
                    var selectedLimits = limits with { MaxRows = remainingRows, MaxCells = remainingCells };
                    var xpt = await PackageCapturedXptReader.ReadAsync(capture, path, encoding, selectedLimits, token).ConfigureAwait(false);
                    var status = xpt.Status switch
                    {
                        CapturedXptStatus.Read => StudyDataReferenceStatus.Read,
                        CapturedXptStatus.InputChanged => StudyDataReferenceStatus.InputChanged,
                        CapturedXptStatus.Missing => StudyDataReferenceStatus.MissingContent,
                        CapturedXptStatus.LimitExceeded => StudyDataReferenceStatus.LimitExceeded,
                        _ => StudyDataReferenceStatus.Unavailable
                    };
                    PackageTrialSummaryFacts? ts = null;
                    if (status == StudyDataReferenceStatus.Read)
                    {
                        ts = PackageTrialSummaryReader.Read(xpt, remainingRows, token);
                        if (xpt.Content is { } content)
                        {
                            remainingRows -= content.Rows.Count;
                            remainingCells -= content.Rows.Sum(row => row.Cells.Count);
                        }
                    }
                    cache.Add(path, (status, status == StudyDataReferenceStatus.Read ? xpt : null, ts));
                    Add(status, path, status == StudyDataReferenceStatus.Read ? xpt : null, ts);

                    void Add(StudyDataReferenceStatus status, string? contentPath = null, CapturedXptFacts? content = null, PackageTrialSummaryFacts? summary = null)
                    {
                        Tick();
                        if (status is not (StudyDataReferenceStatus.Read or StudyDataReferenceStatus.NotSelected) ||
                            status == StudyDataReferenceStatus.Read && content?.Content is not { StructureComplete: true }) complete = false;
                        references.Add(new(reference, status, contentPath, content, summary));
                    }
                }
                documents[^1] = new(document, references);
            }
            await packages.VerifyUnchangedAsync(token).ConfigureAwait(false);
            if (target.InputDigest != inputDigest) return Result(false, []);
        }
        catch (PackageInputChangedException) { return Result(false, []); }
        catch (IOException) { return Result(false, []); }
        catch (StudyDataLimitException) { return Result(false, []); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { return Result(false, []); }
        return Result(complete, documents);

        PackageStudyDataInspection Result(bool finished, IEnumerable<StudyDataDocument> results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(selectionDigest, finished, results);
        }
        void Tick()
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0) throw new StudyDataLimitException();
        }
        bool HasDatasetTag(ParsedXmlElement element, IReadOnlyDictionary<string, ParsedXmlElement> elements)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (element.NamespaceUri.Length == 0 && element.LocalName is "doc-content" or "content-block")
            {
                Tick();
                if (!seen.Add(element.NodePath)) return false;
                foreach (var childPath in element.Children)
                {
                    Tick();
                    if (!elements.TryGetValue(childPath, out var child) || child.NamespaceUri.Length != 0 || child.LocalName != "file-tag") continue;
                    if (child.Attribute("info-type", "") == "ich" && child.Attribute("name", "") is
                        "data-tabulation-dataset-sdtm" or "data-tabulation-dataset-send" or "analysis-dataset-adam") return true;
                }
                if (element.ParentPath is not { } parent || !elements.TryGetValue(parent, out element!)) break;
            }
            return false;
        }
    }

    private sealed class StudyDataLimitException : Exception;
}
