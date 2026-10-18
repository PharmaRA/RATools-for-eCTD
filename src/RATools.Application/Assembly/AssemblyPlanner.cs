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
    {
        var parsed = AssemblyManifest.Parse(csv);
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
        var currentById = currentPlacements.ToDictionary(placement => placement.Id);
        var documentIds = parsed.Rows.Select(row => row.Fields.GetValueOrDefault("documentId"))
            .Where(value => Guid.TryParse(value, out _)).Select(value => Guid.Parse(value!)).ToArray();
        var documents = (await documentRepository.ListByIdsPreferScopedAsync(documentIds, cancellationToken).ConfigureAwait(false))
            .ToDictionary(document => document.Id);

        var rows = new List<AssemblyPlanRow>();
        var seenTargets = new Dictionary<Guid, int>();
        var seenSort = new HashSet<(Guid Node, int Order)>();
        foreach (var row in parsed.Rows)
        {
            var rowDiagnostics = new List<AssemblyDiagnostic>();
            var documentText = row.Fields.GetValueOrDefault("documentId") ?? string.Empty;
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
                if (!seenSort.Add((node.NodeInstanceId, sortOrder)))
                    rowDiagnostics.Add(new("ASSEMBLY_SORT_CONFLICT",
                        $"Another row already uses sort order {sortOrder} on this node.", row.LineNumber));
            }

            if (target is not null && nodeId is not null)
                ResolveTarget(target, nodeId.Value, historical, currentById, row, rowDiagnostics, seenTargets);

            rows.Add(new AssemblyPlanRow(row.LineNumber, documentId?.ToString("D"), nodeId?.ToString("D"), title,
                operation, target, sortOrder,
                rowDiagnostics.Count == 0 ? AssemblyRowStatus.Valid : AssemblyRowStatus.Invalid, rowDiagnostics));
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
        Dictionary<Guid, DocumentPlacement> current, AssemblyManifestRow row,
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
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AssemblyPlan Incomplete(Guid applicationId, string sequenceNumber, long expectedRevision,
        int totalRows, IReadOnlyList<AssemblyDiagnostic> diagnostics) =>
        new(applicationId, sequenceNumber.Trim(), expectedRevision, string.Empty, totalRows, 0, totalRows, diagnostics, []);
}
