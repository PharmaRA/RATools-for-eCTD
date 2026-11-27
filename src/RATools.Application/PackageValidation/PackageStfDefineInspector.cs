using System.Text.Json;
using RATools.Domain.Common;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class PackageStfDefineInspector
{
    internal static async Task<PackageStfDefineInspection> InspectAsync(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, string stfPath, LeafAddress defineLeaf, DefineXmlReadLimits? defineLimits = null,
        XptReadLimits? xptLimits = null, XptTextEncoding encoding = XptTextEncoding.AsciiOnly, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        defineLimits ??= new(); xptLimits ??= new();
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        if (defineLimits.MaxBytes < 0 || defineLimits.MaxNodes < 0 || defineLimits.MaxDepth < 0 || defineLimits.MaxCharacters < 0)
            throw new ArgumentOutOfRangeException(nameof(defineLimits));
        if (xptLimits.MaxBytes < 0 || xptLimits.MaxVariables < 0 || xptLimits.MaxRecordBytes < 0 || xptLimits.MaxRows < 0 || xptLimits.MaxCells < 0)
            throw new ArgumentOutOfRangeException(nameof(xptLimits));
        var target = packages.Target;
        var source = target.SourceId; var digest = target.InputDigest; var manifest = target.Manifest;
        var limits = target.Limits; limits.Validate();
        var limitsDigest = limits.Digest();
        var baseline = packages.Baseline.Digest;
        var selection = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, readerVersion = "stf-define-cross-v2;define-datasets-v1", source, digest,
            manifestDigest = manifest.Digest, limitsDigest, baseline, lifecycle.ProfileSnapshotId,
            rules = context.Catalog.Digest, assets = context.Assets.Digest, stfPath, defineLeaf,
            defineLimits, xptLimits, encoding = encoding.ToString()
        }));
        var work = limits.MaxXmlNodes;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(limits.MaxElapsedSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var token = linked.Token;
        try
        {
            await packages.VerifyUnchangedAsync(token).ConfigureAwait(false);
            if (!SameIdentity()) return Result(StfDefineInspectionStatus.InputChanged);
            if (defineLeaf.ApplicationId != manifest.ApplicationId || defineLeaf.SequenceNumber != manifest.SequenceNumber ||
                !stfPath.StartsWith(manifest.SequenceNumber + "/", StringComparison.Ordinal)) return Result(StfDefineInspectionStatus.StfUnavailable);
            var graph = PackageStfBindingReader.Read(packages, lifecycle, context, token);
            var matches = graph.Documents.Where(document => document.Document.LogicalPath == stfPath).Take(2).ToArray();
            if (!graph.TargetScopeComplete || matches.Length != 1) return Result(StfDefineInspectionStatus.StfUnavailable);
            var stf = matches[0];
            if (!stf.IsTarget || stf.SourceId != source || stf.InputDigest != digest || !stf.StructureComplete ||
                stf.Owners.Count == 0 || stf.Owners.Any(owner => !owner.Qualified) ||
                !stf.Owners.Any(owner => owner.Leaf.Address?.SequenceNumber == manifest.SequenceNumber)) return Result(StfDefineInspectionStatus.StfUnavailable);
            var elements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in stf.Document.Elements)
            {
                Tick();
                if (!elements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != stfPath || element.Location.NodePath != element.NodePath)
                    return Result(StfDefineInspectionStatus.StfUnavailable);
            }
            var roots = elements.Values.Where(element => element.ParentPath is null).Take(2).ToArray();
            if (roots.Length != 1 || roots[0].LocalName != "study" || roots[0].NamespaceUri != "http://www.ich.org/ectd")
                return Result(StfDefineInspectionStatus.StfUnavailable);
            var identifiers = Children(roots[0], "study-identifier");
            var ids = identifiers.Count == 1 ? Children(identifiers[0], "study-id") : [];
            var studyId = ids.Count == 1 && ids[0].Children.Count == 0 ? ids[0].Text : null;
            var xml = lifecycle.XmlInspections.Single(item => item.InputDigest == digest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId);
            var backbones = xml.Backbones.Where(item => item.LogicalPath == manifest.SequenceNumber + "/index.xml").Take(2).ToArray();
            if (backbones.Length != 1) return Result(StfDefineInspectionStatus.StfUnavailable);
            var backbone = backbones[0];
            if (backbone.Kind != BackboneKind.Ich || backbone.ProfileSnapshotId != lifecycle.ProfileSnapshotId)
                return Result(StfDefineInspectionStatus.StfUnavailable);
            var leafElements = new Dictionary<string, ParsedXmlElement>(StringComparer.Ordinal);
            foreach (var element in backbone.Elements)
            {
                Tick();
                if (!leafElements.TryAdd(element.NodePath, element) || element.Location.LogicalPath != backbone.LogicalPath ||
                    element.Location.NodePath != element.NodePath) return Result(StfDefineInspectionStatus.StfUnavailable);
            }
            var byPath = new Dictionary<string, List<StfDocumentReference>>(StringComparer.Ordinal);
            var definePaths = new HashSet<string>(StringComparer.Ordinal);
            var defineUnknown = false;
            var unknownReferences = false;
            foreach (var reference in stf.References)
            {
                Tick();
                var path = ContentPath(reference);
                if (path is null) unknownReferences = true;
                else
                {
                    if (!byPath.TryGetValue(path, out var references)) byPath.Add(path, references = []);
                    references.Add(reference);
                }
                if (reference.Target == defineLeaf)
                {
                    if (path is null) defineUnknown = true;
                    else definePaths.Add(path);
                }
            }
            if (defineUnknown || definePaths.Count != 1) return Result(StfDefineInspectionStatus.DefineUnavailable);
            var define = await PackageDefineDatasetInspector.InspectAsync(target, definePaths.Single(), defineLimits, xptLimits, encoding, token).ConfigureAwait(false);
            if (!SameIdentity() || define.Status == DefineDatasetInspectionStatus.InputChanged) return Result(StfDefineInspectionStatus.InputChanged);
            if (define.Status != DefineDatasetInspectionStatus.Read || !define.TraversalComplete)
                return Result(define.Status == DefineDatasetInspectionStatus.LimitExceeded ? StfDefineInspectionStatus.LimitExceeded : StfDefineInspectionStatus.DefineUnavailable);
            var datasets = new List<StfDefineDataset>();
            var declarationIds = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var dataset in define.Datasets)
            {
                Tick();
                if (dataset.Declaration.Oid is { Length: > 0 } oid)
                    declarationIds[oid] = declarationIds.GetValueOrDefault(oid) + 1;
            }
            foreach (var dataset in define.Datasets)
            {
                Tick();
                var references = dataset.LogicalPath is { } path ? byPath.GetValueOrDefault(path) ?? [] : [];
                var leaves = new HashSet<LeafAddress>();
                foreach (var reference in references) { Tick(); leaves.Add(reference.Target!); }
                var status = leaves.Count > 1 ? StfDatasetReferenceStatus.MultipleLeaves : leaves.Count == 1 ? StfDatasetReferenceStatus.Referenced :
                    dataset.LogicalPath is null || unknownReferences ? StfDatasetReferenceStatus.Unknown : StfDatasetReferenceStatus.NotReferenced;
                var studyIds = new List<DatasetStudyIdObservation>();
                var complete = false;
                if (dataset.Xpt?.Content is { Status: XptReadStatus.Read, StructureComplete: true } content)
                {
                    var columns = new List<int>();
                    for (var index = 0; index < content.Variables.Count; index++)
                    { Tick(); if (content.Variables[index].Name == "STUDYID") columns.Add(index); }
                    if (columns.Count == 1 && content.Variables[columns[0]].Type == 2)
                    {
                        complete = content.MinPossibleRows == content.MaxPossibleRows && content.MaxPossibleRows == content.Rows.Count;
                        foreach (var row in content.Rows)
                        {
                            Tick();
                            var cell = columns[0] < row.Cells.Count ? row.Cells[columns[0]] : null;
                            var decoded = cell is { Decoded: true, Text: not null };
                            studyIds.Add(new(row.Index, row.Certain, decoded, decoded ? cell!.Text : null));
                            complete &= row.Certain && decoded;
                        }
                    }
                }
                var member = dataset.Xpt?.Content is { Status: XptReadStatus.Read, StructureComplete: true } xpt ? xpt.MemberName : null;
                datasets.Add(new(dataset, status, references, complete, studyIds)
                {
                    Identity = new(dataset.Declaration.Oid is { Length: > 0 } oid && declarationIds[oid] == 1,
                        CompareName(dataset.Declaration.Name, member), CompareName(dataset.Declaration.SasDatasetName, member))
                });
            }
            await packages.VerifyUnchangedAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!SameIdentity()) return Result(StfDefineInspectionStatus.InputChanged);
            return new(StfDefineInspectionStatus.Read, selection, stf, studyId, define, datasets);

            List<ParsedXmlElement> Children(ParsedXmlElement parent, string name)
            {
                var children = new List<ParsedXmlElement>();
                foreach (var child in parent.Children)
                { Tick(); if (elements.TryGetValue(child, out var value) && value.ParentPath == parent.NodePath && value.LocalName == name && value.NamespaceUri.Length == 0) children.Add(value); }
                return children;
            }
            string? ContentPath(StfDocumentReference reference)
            {
                if (reference.Status != StfReferenceStatus.Resolved || reference.Target is not { } address || reference.Leaf is not { } leaf ||
                    address.ApplicationId != manifest.ApplicationId || address.SequenceNumber != manifest.SequenceNumber || address.BackboneRelativePath != "index.xml" ||
                    leaf.Address != address || !leafElements.TryGetValue(leaf.NodePath, out var element) || element.LocalName != "leaf" || element.NamespaceUri.Length != 0)
                    return null;
                if (Explicit(element, "ID", "") != address.LeafId || Explicit(element, "operation", "") != leaf.Operation ||
                    leaf.Operation is not ("new" or "append" or "replace") || Explicit(element, "href", "http://www.w3c.org/1999/xlink") is not { } href || href != leaf.Href)
                    return null;
                if (href.Length > limits.MaxPathLength || href.Any(character => character is < '!' or > '~') || href.IndexOfAny(['%', '?', '#', ':', '\\']) >= 0 || href.StartsWith('/')) return null;
                try
                {
                    var resolved = PackageLogicalPath.ResolveReference(backbone.LogicalPath, href);
                    return resolved.LogicalPath is { } path && path.StartsWith(manifest.SequenceNumber + "/", StringComparison.Ordinal) ?
                        PackageLogicalPath.Validate(path, limits.MaxPathLength, limits.MaxPathDepth) : null;
                }
                catch (ArgumentException) { return null; }
            }
            string? Explicit(ParsedXmlElement element, string name, string ns)
            {
                string? value = null; var count = 0;
                foreach (var attribute in element.Attributes.Values)
                {
                    Tick();
                    if (attribute.LocalName != name || attribute.NamespaceUri != ns) continue;
                    count++;
                    if (!attribute.IsDefault && attribute.ExplicitValue == attribute.Value) value = attribute.ExplicitValue;
                }
                return count == 1 ? value : null;
            }
        }
        catch (PackageInputChangedException) { return Result(StfDefineInspectionStatus.InputChanged); }
        catch (CrossLimitException) { return Result(StfDefineInspectionStatus.LimitExceeded); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { return Result(StfDefineInspectionStatus.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { return Result(StfDefineInspectionStatus.Unavailable); }

        void Tick() { token.ThrowIfCancellationRequested(); if (work-- <= 0) throw new CrossLimitException(); }
        bool SameIdentity() => target.SourceId == source && target.InputDigest == digest && target.Manifest.Digest == manifest.Digest &&
            target.Limits.Digest() == limitsDigest && packages.Baseline.Digest == baseline;
        PackageStfDefineInspection Result(StfDefineInspectionStatus status)
        { cancellationToken.ThrowIfCancellationRequested(); return new(status, selection); }
    }
    private static DatasetNameComparison CompareName(string? declared, string? actual) =>
        string.IsNullOrEmpty(declared) || string.IsNullOrEmpty(actual) ? DatasetNameComparison.Unknown :
        string.Equals(declared, actual, StringComparison.Ordinal) ? DatasetNameComparison.Equal : DatasetNameComparison.Different;
    private sealed class CrossLimitException : Exception;
}
