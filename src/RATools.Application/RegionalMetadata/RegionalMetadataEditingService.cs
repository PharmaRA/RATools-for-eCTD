using System.Text.Json;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.RegionalMetadata;

namespace RATools.Application.RegionalMetadata;

public sealed partial class RegionalMetadataService
{
    public async Task<RegionalMetadataSchemaDto> GetSchemaAsync(Guid appId, string? standardsSnapshotId = null, CancellationToken cancellationToken = default)
    {
        var app = await ApplicationAsync(appId, null, cancellationToken).ConfigureAwait(false);
        var snapshot = standardsSnapshotId is null ? await snapshots.CaptureAsync(app.EctdTemplateKey, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await SnapshotAsync(app, standardsSnapshotId, Region(app), cancellationToken).ConfigureAwait(false);
        return RegionalMetadataSchema.Create(snapshot, Region(app));
    }
    public async Task<RegionalMetadataRevision?> GetScopedHeadAsync(Guid appId, string? sequence, CancellationToken cancellationToken = default)
    {
        _ = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var revision = await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false); revision?.Verify(); return revision;
    }
    public async Task<RegionalMetadataRevision> GetScopedRevisionAsync(Guid appId, string? sequence, Guid id, CancellationToken cancellationToken = default)
    {
        _ = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var revision = await GetRevisionAsync(id, cancellationToken).ConfigureAwait(false);
        if (revision.ApplicationId != appId || revision.SequenceNumber != sequence) throw new RegionalMetadataScopeException("The revision is outside the exact requested application/sequence scope.");
        return revision;
    }
    public async Task<RegionalMetadataRevision> SaveMixedAsync(Guid appId, string sequence, long? expectedRevision, RegionalMetadataUpdate update,
        SequencePublishingMetadata shared, CancellationToken cancellationToken = default)
    {
        if (update.Content is null || string.IsNullOrWhiteSpace(update.StandardsSnapshotId)) throw new ArgumentException("Typed regional content and an exact standards snapshot are required.");
        await using var mutation = await mutations.AcquireAsync(appId, sequence, expectedRevision, cancellationToken).ConfigureAwait(false);
        var app = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var previous = await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        return await SaveWithinAsync(app, sequence, mutation, previous, update.StandardsSnapshotId, update.Content,
            update.NodeContexts ?? previous?.NodeContexts ?? [], update.DocumentContexts ?? previous?.DocumentContexts ?? [],
            previous?.InheritedDefaultsId, previous?.InheritedDefaultsDigest, null, null, null, shared, cancellationToken).ConfigureAwait(false);
    }
    public async Task<RegionalMetadataPreflightDto> PreflightDefaultsAsync(Guid appId, string snapshotId, RegionalApplicationDefaultsContent defaults,
        CancellationToken cancellationToken = default)
    {
        RegionalMetadataJson.VerifyBudget(defaults);
        var app = await ApplicationAsync(appId, null, cancellationToken).ConfigureAwait(false);
        var snapshot = await SnapshotAsync(app, snapshotId, defaults.RegionKey, cancellationToken).ConfigureAwait(false);
        // Construction applies the same structural invariants as persistence, without appending evidence.
        var draft = RegionalMetadataRevision.Create(Guid.NewGuid(), appId, null, 1, null, snapshotId, null, defaults, [], [], null, null, [], [], null, null, Now);
        return Preflight(snapshotId, RegionalMetadataValidation.Defaults(draft.Defaults!, snapshot), draft.Defaults!, RegionalMetadataSchema.Create(snapshot, Region(app)));
    }
    public async Task<RegionalMetadataPreflightDto> PreflightAsync(Guid appId, string sequence, RegionalMetadataUpdate update, CancellationToken cancellationToken = default)
    {
        if (update.Content is null || string.IsNullOrWhiteSpace(update.StandardsSnapshotId)) throw new ArgumentException("Typed regional content and an exact standards snapshot are required.");
        RegionalMetadataJson.VerifyBudget(update);
        var app = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var snapshot = await SnapshotAsync(app, update.StandardsSnapshotId, update.Content.RegionKey, cancellationToken).ConfigureAwait(false);
        var previous = await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var contexts = update.NodeContexts ?? previous?.NodeContexts ?? []; var documents = update.DocumentContexts ?? previous?.DocumentContexts ?? [];
        var draft = RegionalMetadataRevision.Create(Guid.NewGuid(), appId, sequence, 1, 1, snapshot.Id, update.Content, null, contexts, documents, null, null, [], [], null, null, Now);
        var sections = await ScopeAsync(app, sequence, draft.NodeContexts, draft.DocumentContexts, cancellationToken).ConfigureAwait(false);
        var diagnostics = RegionalMetadataValidation.Sequence(draft.Content!, snapshot, app.Sequences.Select(item => item.SequenceNumber).ToHashSet(StringComparer.Ordinal), sequence,
            draft.NodeContexts, draft.DocumentContexts, sections);
        return Preflight(snapshot.Id, diagnostics, new { draft.Content, draft.NodeContexts, draft.DocumentContexts }, RegionalMetadataSchema.Create(snapshot, Region(app)));
    }
    public async Task<RegionalMetadataPreviewDto> PreviewInheritanceAsync(Guid appId, string sequence, long? expectedRevision, Guid defaultsRevisionId,
        CancellationToken cancellationToken = default)
    {
        // Hold the same ownership/CAS/finalized guard as the eventual mutation, but never commit.
        await using var mutation = await mutations.AcquireAsync(appId, sequence, expectedRevision, cancellationToken).ConfigureAwait(false);
        _ = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var source = await GetScopedRevisionAsync(appId, null, defaultsRevisionId, cancellationToken).ConfigureAwait(false);
        var previous = await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        if (previous is not null && previous.RegionKey != source.RegionKey) throw new RegionalMetadataScopeException("Defaults and sequence metadata use different regions.");
        var content = Inherit(previous?.Content, source.Defaults!);
        return Preview(source, mutation.Revision, previous, content, previous?.NodeContexts ?? [], previous?.DocumentContexts ?? []);
    }
    public async Task<RegionalMetadataPreviewDto> PreviewNodeContextCopyAsync(Guid appId, string sequence, long? expectedRevision, Guid sourceRevisionId,
        Guid nodeInstanceId, IReadOnlyList<Guid> placementIds, CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(appId, sequence, expectedRevision, cancellationToken).ConfigureAwait(false);
        var app = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var source = await GetScopedRevisionAsync(appId, sequence, sourceRevisionId, cancellationToken).ConfigureAwait(false);
        var node = source.NodeContexts.SingleOrDefault(item => item.NodeInstanceId == nodeInstanceId) ?? throw new RegionalMetadataScopeException("The exact source has no context for this node.");
        if (placementIds is null || placementIds.Count is < 1 or > 1000 || placementIds.Distinct().Count() != placementIds.Count) throw new ArgumentException("Choose distinct bounded placement identities.");
        var previous = await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false) ?? throw new RegionalMetadataScopeException("The sequence has no regional metadata to receive node defaults.");
        var replacements = placementIds.ToDictionary(id => id, id => new RegionalDocumentContext(id, nodeInstanceId, node.Country, node.Language, node.ProductInformationType, node.FormType));
        var documents = previous.DocumentContexts.Select(item => replacements.GetValueOrDefault(item.PlacementId) ?? item)
            .Concat(placementIds.Where(id => previous.DocumentContexts.All(item => item.PlacementId != id)).Select(id => replacements[id])).ToArray();
        _ = await ScopeAsync(app, sequence, previous.NodeContexts, documents, cancellationToken).ConfigureAwait(false);
        return Preview(source, mutation.Revision, previous, previous.Content!, previous.NodeContexts, documents);
    }
    private static RegionalMetadataPreviewDto Preview(RegionalMetadataRevision source, long revision, RegionalMetadataRevision? before, RegionalMetadataContent after,
        IReadOnlyList<RegionalNodeContext> nodes, IReadOnlyList<RegionalDocumentContext> documents)
    {
        var old = before is null ? new Dictionary<string, FieldValue>() : Fields(JsonSerializer.SerializeToElement(new { before.Content, before.NodeContexts, before.DocumentContexts }, JsonOptions));
        var current = Fields(JsonSerializer.SerializeToElement(new { Content = after, NodeContexts = nodes, DocumentContexts = documents }, JsonOptions));
        var diffs = old.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal).Where(path => old.GetValueOrDefault(path)?.Value != current.GetValueOrDefault(path)?.Value)
            .Select(path => new RegionalMetadataFieldDiff(path, old.GetValueOrDefault(path)?.Value, current.GetValueOrDefault(path)?.Value)).ToArray();
        return new(source.Id, source.Digest, revision, after, nodes, documents, diffs);
    }
    private static RegionalMetadataPreflightDto Preflight(string snapshotId, IReadOnlyList<RegionalMetadataDiagnostic> diagnostics, object content, RegionalMetadataSchemaDto schema)
    {
        var flattened = new Dictionary<string, RegionalSourceReferenceDto?>();
        void Visit(IEnumerable<RegionalFieldDescriptor> fields, string prefix)
        {
            foreach (var field in fields)
            {
                var path = prefix + field.Key; flattened[path] = field.Source;
                if (field.Fields.Count > 0) Visit(field.Fields, path + (field.Kind == "collection" ? "[]." : "."));
            }
        }
        Visit(schema.SequenceFields, ""); Visit(schema.DefaultsFields, ""); Visit(schema.NodeContextFields, "nodeContexts[]."); Visit(schema.DocumentContextFields, "documentContexts[].");
        RegionalSourceReferenceDto? Source(string path)
        {
            var normalized = System.Text.RegularExpressions.Regex.Replace(path.StartsWith("content.", StringComparison.Ordinal) ? path[8..] : path, "\\[\\d+\\]", "[]",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return flattened.GetValueOrDefault(normalized) ?? (schema.Sources.Count > 0 ? schema.Sources[0] : null);
        }
        var fields = Fields(JsonSerializer.SerializeToElement(content, JsonOptions));
        return new(snapshotId, diagnostics.Select(item => new RegionalDiagnosticDto(item.Code, item.FieldPath, item.Message, Source(item.FieldPath))).ToArray(),
            fields.Select(pair => new RegionalMetadataSummaryEntry(pair.Key, pair.Value.Value, Source(pair.Key))).ToArray());
    }
}
