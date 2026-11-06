using System.Globalization;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Documents;
using RATools.Domain.Documents;

namespace RATools.Application.Assembly;

// The planner resolves every row against the exact workspace revision before anything is
// written. It never guesses a node from a section number, never accepts a lifecycle
// target from the current sequence and never invents a trust state; unresolved rows are
// reported as invalid with their reason so the operator can fix them and re-preview.
public sealed class AssemblyPlanner(
    IApplicationRepository applicationRepository,
    IDocumentRepository documentRepository,
    IDocumentPlacementRepository placementRepository,
    ICtdNodeRepository nodeRepository,
    IDocumentStorageBoundary storageBoundary,
    IWorkspaceRevisionStore revisions)
{
    private static readonly string[] Operations = ["new", "replace", "append", "delete"];

    public async Task<AssemblyPlan> PlanAsync(Guid applicationId, string sequenceNumber, long expectedRevision,
        string csv, CancellationToken cancellationToken = default)
        => await PlanAsync(applicationId, sequenceNumber, expectedRevision, AssemblyManifest.Parse(csv), cancellationToken).ConfigureAwait(false);

    public async Task<AssemblyPlan> PlanAsync(Guid applicationId, string sequenceNumber, long expectedRevision,
        AssemblyManifestParseResult parsed, CancellationToken cancellationToken = default)
    {
        var diagnostics = parsed.Diagnostics.ToList();
        if (!parsed.Succeeded)
            return Incomplete(applicationId, sequenceNumber, expectedRevision, parsed.Rows.Count, diagnostics);
        var currentRevision = await revisions.GetRevisionAsync(applicationId, sequenceNumber, cancellationToken)
            .ConfigureAwait(false);
        if (currentRevision is null)
        {
            diagnostics.Add(new("ASSEMBLY_SEQUENCE_NOT_FOUND", $"Sequence {sequenceNumber} does not exist on application {applicationId}."));
            return Incomplete(applicationId, sequenceNumber, expectedRevision, parsed.Rows.Count, diagnostics);
        }
        if (currentRevision != expectedRevision)
        {
            diagnostics.Add(new("ASSEMBLY_REVISION_CONFLICT",
                $"The workspace revision is {currentRevision}, not the requested {expectedRevision}. Regenerate the preview."));
            return Incomplete(applicationId, sequenceNumber, expectedRevision, parsed.Rows.Count, diagnostics);
        }
        var application = await applicationRepository.GetAsync(applicationId, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            diagnostics.Add(new("ASSEMBLY_APPLICATION_NOT_FOUND", $"Application {applicationId} was not found."));
            return Incomplete(applicationId, sequenceNumber, expectedRevision, parsed.Rows.Count, diagnostics);
        }

        var workspace = await nodeRepository.GetSequenceAsync(applicationId, sequenceNumber, cancellationToken).ConfigureAwait(false);
        var nodes = workspace?.Nodes.ToDictionary(node => node.NodeInstanceId) ?? [];
        var currentPlacements = await placementRepository.ListBySequenceAsync(applicationId, sequenceNumber, cancellationToken)
            .ConfigureAwait(false);
        var allPlacements = await placementRepository.ListByApplicationAsync(applicationId, cancellationToken).ConfigureAwait(false);
        var historical = allPlacements.Where(placement =>
            string.CompareOrdinal(placement.SequenceNumber, sequenceNumber) < 0).ToDictionary(placement => placement.Id);
        var effectiveness = ReplayHistory(historical.Values);
        var currentById = currentPlacements.ToDictionary(placement => placement.Id);
        var documentIds = parsed.Rows.Select(row => SourceToken(row.Fields.GetValueOrDefault("sourceFile")) ?? row.Fields.GetValueOrDefault("documentId"))
            .Where(value => Guid.TryParse(value, out _)).Select(value => Guid.Parse(value!)).ToArray();
        var documents = (await documentRepository.ListByIdsPreferScopedAsync(documentIds, cancellationToken).ConfigureAwait(false))
            .ToDictionary(document => document.Id);

        var rows = new List<AssemblyPlanRow>();
        var seenTargets = new Dictionary<Guid, int>();
        var seenSort = new HashSet<(Guid Node, int Order)>();
        var nextSort = currentPlacements.Where(item => item.NodeInstanceId is not null)
            .GroupBy(item => item.NodeInstanceId!.Value).ToDictionary(group => group.Key, group => group.Max(item => item.SortOrder) + 1);
        foreach (var row in parsed.Rows)
        {
            var rowDiagnostics = row.Diagnostics?.ToList() ?? [];
            var legacyDocument = row.Fields.GetValueOrDefault("documentId") ?? string.Empty;
            var sourceFile = NullIfBlank(row.Fields.GetValueOrDefault("sourceFile"));
            var sourceToken = SourceToken(sourceFile);
            var documentText = sourceToken ?? legacyDocument;
            if (sourceFile is not null && sourceToken is null)
                rowDiagnostics.Add(new("ASSEMBLY_SOURCE_TOKEN_INVALID", "sourceFile must be an authorized uploaded document token (document:UUID or UUID), never a server path.", row.LineNumber));
            if (sourceToken is not null && legacyDocument.Length > 0 && !string.Equals(sourceToken, legacyDocument, StringComparison.OrdinalIgnoreCase))
                rowDiagnostics.Add(new("ASSEMBLY_SOURCE_ALIAS_CONFLICT", "sourceFile and documentId refer to different uploaded documents.", row.LineNumber));
            var fileName = NullIfBlank(row.Fields.GetValueOrDefault("fileName"));
            var nodeText = row.Fields.GetValueOrDefault("nodeInstanceId") ?? string.Empty;
            var operation = (row.Fields.GetValueOrDefault("operation") ?? string.Empty).Trim().ToLowerInvariant();
            var title = NullIfBlank(row.Fields.GetValueOrDefault("title"));
            var target = NullIfBlank(row.Fields.GetValueOrDefault("targetLeafAddress"));
            var sortText = row.Fields.GetValueOrDefault("sortOrder") ?? string.Empty;

            if (!Operations.Contains(operation, StringComparer.Ordinal))
                rowDiagnostics.Add(new("ASSEMBLY_OPERATION_INVALID", "Use new, replace, append or delete.", row.LineNumber));
            if (operation == "delete" && documentText.Length > 0)
                rowDiagnostics.Add(new("ASSEMBLY_DELETE_HAS_CONTENT", "A delete row is a lifecycle event; it must not name a document.", row.LineNumber));
            if (operation is "replace" or "append" or "delete" && target is null)
                rowDiagnostics.Add(new("ASSEMBLY_TARGET_REQUIRED", "This operation requires an explicit targetLeafAddress.", row.LineNumber));
            var sortOrder = 0;
            if (sortText.Length > 0 && (!int.TryParse(sortText, NumberStyles.None, CultureInfo.InvariantCulture, out sortOrder) || sortOrder < 0))
                rowDiagnostics.Add(new("ASSEMBLY_SORT_INVALID", "sortOrder must be a nonnegative integer.", row.LineNumber));

            Guid? documentId = null;
            if (operation != "delete")
            {
                if (!Guid.TryParse(documentText, out var parsedDocument))
                    rowDiagnostics.Add(new("ASSEMBLY_DOCUMENT_REQUIRED", "Each non-delete row must name the uploaded documentId.", row.LineNumber));
                else if (!documents.TryGetValue(parsedDocument, out var document))
                    rowDiagnostics.Add(new("ASSEMBLY_DOCUMENT_NOT_FOUND", $"Document {parsedDocument} was not found.", row.LineNumber));
                else
                {
                    try { _ = storageBoundary.EnsureDocumentOwnedBySequence(document, application, sequenceNumber); }
                    catch (DocumentStorageBoundaryException)
                    {
                        rowDiagnostics.Add(new("ASSEMBLY_DOCUMENT_OUT_OF_SCOPE",
                            $"Document {parsedDocument} is not stored inside sequence {sequenceNumber} of this application.", row.LineNumber));
                    }
                    documentId = parsedDocument;
                }
            }

            Guid? nodeId = null;
            if (!Guid.TryParse(nodeText, out var parsedNode))
                rowDiagnostics.Add(new("ASSEMBLY_NODE_REQUIRED", "Each row must name the exact node instance; a section number is not enough.", row.LineNumber));
            else if (!nodes.TryGetValue(parsedNode, out var node))
                rowDiagnostics.Add(new("ASSEMBLY_NODE_NOT_FOUND", $"Node {parsedNode} is not in this sequence.", row.LineNumber));
            else if (!node.AllowsLeaves)
                rowDiagnostics.Add(new("ASSEMBLY_NODE_NO_LEAVES", $"Node {parsedNode} cannot receive document leaves.", row.LineNumber));
            else if (node.MetadataStatus != RATools.Domain.Ctd.NodeMetadataStatus.Complete)
                rowDiagnostics.Add(new("ASSEMBLY_NODE_INCOMPLETE", $"Node {parsedNode} has incomplete identity metadata.", row.LineNumber));
            else
            {
                nodeId = node.NodeInstanceId;
                if (sortText.Length == 0) sortOrder = nextSort.GetValueOrDefault(node.NodeInstanceId);
                nextSort[node.NodeInstanceId] = checked(sortOrder + 1);
                if (!seenSort.Add((node.NodeInstanceId, sortOrder)))
                    rowDiagnostics.Add(new("ASSEMBLY_SORT_CONFLICT",
                        $"Another row already uses sort order {sortOrder} on this node.", row.LineNumber));
            }

            if (target is not null && nodeId is not null)
                ResolveTarget(target, nodeId.Value, historical, currentById, effectiveness, row, rowDiagnostics, seenTargets);

            rows.Add(new AssemblyPlanRow(row.LineNumber, documentId?.ToString("D"), nodeId?.ToString("D"), title,
                operation, target, sortOrder,
                rowDiagnostics.Count == 0 ? AssemblyRowStatus.Valid : AssemblyRowStatus.Invalid, rowDiagnostics,
                sourceFile, fileName));
        }

        var digest = AssemblyManifest.ComputeDigest(applicationId, sequenceNumber, expectedRevision, rows);
        return new(applicationId, sequenceNumber, expectedRevision, digest, rows.Count,
            rows.Count(row => row.Status == AssemblyRowStatus.Valid), rows.Count(row => row.Status == AssemblyRowStatus.Invalid),
            diagnostics, rows);
    }

    // A target is accepted only as the id of a readable earlier-sequence placement on the
    // same node. Same-sequence targets need explicit policy and are refused rather than
    // silently resolved by file name.
    private static void ResolveTarget(string target, Guid nodeId,
        Dictionary<Guid, DocumentPlacement> historical,
        Dictionary<Guid, DocumentPlacement> current, IReadOnlyDictionary<Guid, HistoricalLeafState> effectiveness, AssemblyManifestRow row,
        List<AssemblyDiagnostic> diagnostics, Dictionary<Guid, int> seenTargets)
    {
        if (!Guid.TryParse(target, out var targetId))
        {
            diagnostics.Add(new("ASSEMBLY_TARGET_INVALID",
                $"Target '{target}' is not a supported address; use the historical placement id.", row.LineNumber));
            return;
        }
        if (current.ContainsKey(targetId))
        {
            diagnostics.Add(new("ASSEMBLY_TARGET_SAME_SEQUENCE",
                "Same-sequence lifecycle targets require explicit policy and are not supported by bulk assembly.", row.LineNumber));
            return;
        }
        if (!historical.TryGetValue(targetId, out var placement))
        {
            diagnostics.Add(new("ASSEMBLY_TARGET_NOT_FOUND", $"Placement {targetId} is not an earlier-sequence leaf of this application.", row.LineNumber));
            return;
        }
        if (seenTargets.TryGetValue(targetId, out var firstLine))
        {
            diagnostics.Add(new("ASSEMBLY_TARGET_CONFLICT", $"Target '{target}' is already used by row {firstLine}.", row.LineNumber));
            return;
        }
        seenTargets[targetId] = row.LineNumber;
        if (placement.NodeInstanceId != nodeId)
            diagnostics.Add(new("ASSEMBLY_TARGET_NODE_MISMATCH", "The target leaf belongs to a different node.", row.LineNumber));
        else if (placement.Operation == DocumentPlacementOperation.Delete)
            diagnostics.Add(new("ASSEMBLY_TARGET_DELETED", "The target leaf was already deleted; choose the current leaf.", row.LineNumber));
        else if (effectiveness.GetValueOrDefault(targetId) == HistoricalLeafState.Ineffective)
            diagnostics.Add(new("ASSEMBLY_TARGET_NOT_EFFECTIVE", "The exact historical leaf has already been replaced or deleted.", row.LineNumber));
        else if (effectiveness.GetValueOrDefault(targetId) != HistoricalLeafState.Current)
            diagnostics.Add(new("ASSEMBLY_TARGET_EFFECTIVENESS_UNKNOWN", "An append branch or unresolved historical event requires an explicit cumulative lifecycle policy.", row.LineNumber));
    }

    internal enum HistoricalLeafState { Unknown, Current, Ineffective, AppendMember, DeleteEvent }

    // Sequence boundaries matter: same-sequence combinations and conflicting events
    // cannot establish an effective target. Append members stay unqualified; replacing
    // one must never infer cascading invalidation of the entire append group.
    internal static IReadOnlyDictionary<Guid, HistoricalLeafState> ReplayHistory(IEnumerable<DocumentPlacement> historical)
    {
        var states = new Dictionary<Guid, HistoricalLeafState>();
        var placements = historical.ToDictionary(item => item.Id);
        foreach (var sequence in placements.Values.GroupBy(item => item.SequenceNumber).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var prior = new Dictionary<Guid, HistoricalLeafState>(states);
            var unresolvedTarget = false;
            var sameSequenceTargets = sequence.Where(item => item.Operation != DocumentPlacementOperation.New &&
                    item.LifecycleTargetPlacementId is { } target && placements.TryGetValue(target, out var targetPlacement) &&
                    targetPlacement.SequenceNumber == sequence.Key)
                .Select(item => item.LifecycleTargetPlacementId!.Value).ToHashSet();
            var conflicts = sequence.Where(item => item.Operation != DocumentPlacementOperation.New && item.LifecycleTargetPlacementId is not null)
                .GroupBy(item => item.LifecycleTargetPlacementId!.Value).Where(group => group.Count() > 1)
                .Select(group => group.Key).ToHashSet();
            foreach (var item in sequence)
                states[item.Id] = item.Operation == DocumentPlacementOperation.New && item.LifecycleTargetPlacementId is null
                    ? HistoricalLeafState.Current : item.Operation == DocumentPlacementOperation.Delete
                        ? HistoricalLeafState.DeleteEvent : HistoricalLeafState.Unknown;
            foreach (var item in sequence.Where(item => item.Operation != DocumentPlacementOperation.New))
            {
                if (item.LifecycleTargetPlacementId is not { } target)
                {
                    unresolvedTarget = true;
                    continue;
                }
                if (!prior.TryGetValue(target, out var state))
                {
                    if (!sameSequenceTargets.Contains(target)) unresolvedTarget = true;
                    continue;
                }
                if (placements[target].NodeInstanceId != item.NodeInstanceId)
                {
                    if (state is HistoricalLeafState.Current or HistoricalLeafState.AppendMember) states[target] = HistoricalLeafState.Unknown;
                    continue;
                }
                if (conflicts.Contains(target))
                {
                    if (state is HistoricalLeafState.Current or HistoricalLeafState.AppendMember) states[target] = HistoricalLeafState.Unknown;
                    continue;
                }
                if (state != HistoricalLeafState.Current) continue;
                if (item.Operation is DocumentPlacementOperation.Replace or DocumentPlacementOperation.Delete)
                {
                    states[target] = HistoricalLeafState.Ineffective;
                    if (item.Operation == DocumentPlacementOperation.Replace) states[item.Id] = HistoricalLeafState.Current;
                }
                else if (item.Operation == DocumentPlacementOperation.Append)
                    states[target] = states[item.Id] = HistoricalLeafState.AppendMember;
            }
            foreach (var target in sameSequenceTargets)
                if (states[target] is HistoricalLeafState.Current or HistoricalLeafState.AppendMember)
                    states[target] = HistoricalLeafState.Unknown;
            if (unresolvedTarget)
                foreach (var id in states.Keys.ToArray())
                    if (states[id] is HistoricalLeafState.Current or HistoricalLeafState.AppendMember)
                        states[id] = HistoricalLeafState.Unknown;
        }
        return states;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? SourceToken(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var value = source.StartsWith("document:", StringComparison.Ordinal) ? source[9..] : source;
        return Guid.TryParse(value, out var id) ? id.ToString("D") : null;
    }

    private static AssemblyPlan Incomplete(Guid applicationId, string sequenceNumber, long expectedRevision,
        int totalRows, IReadOnlyList<AssemblyDiagnostic> diagnostics) =>
        new(applicationId, sequenceNumber.Trim(), expectedRevision, string.Empty, totalRows, 0, totalRows, diagnostics, []);
}
