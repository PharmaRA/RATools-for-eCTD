using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Common;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Standards;

namespace RATools.Application.RegionalMetadata;

public sealed partial class RegionalMetadataService(IRegionalMetadataStore store, IApplicationRepository applications, ICtdNodeRepository nodes,
    IDocumentPlacementRepository placements, StandardsSnapshotService snapshots, WorkspaceMutationCoordinator mutations,
    IWorkspaceRevisionStore revisions, IPersistenceTransaction transactions, TimeProvider? clock = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    public Task<RegionalMetadataRevision?> GetApplicationAsync(Guid appId, CancellationToken cancellationToken = default) => store.GetHeadAsync(appId, null, cancellationToken);
    public Task<RegionalMetadataRevision?> GetSequenceAsync(Guid appId, string sequence, CancellationToken cancellationToken = default) => store.GetHeadAsync(appId, sequence, cancellationToken);
    public async Task<RegionalMetadataRevision> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default)
    { var revision = await store.GetRevisionAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Regional revision was not found."); revision.Verify(); return revision; }

    public async Task<RegionalMetadataRevision> SaveApplicationAsync(Guid appId, long expectedDefaultsRevision, string snapshotId,
        RegionalApplicationDefaultsContent defaults, CancellationToken cancellationToken = default)
    {
        await using var held = await revisions.LockApplicationAsync(appId, cancellationToken).ConfigureAwait(false);
        RegionalMetadataJson.VerifyBudget(defaults);
        var app = await ApplicationAsync(appId, null, cancellationToken).ConfigureAwait(false); var snapshot = await SnapshotAsync(app, snapshotId, defaults.RegionKey, cancellationToken).ConfigureAwait(false);
        defaults = RegionalMetadataRevision.Create(Guid.NewGuid(), appId, null, 1, null, snapshotId, null, defaults, [], [], null, null, [], [], null, null, Now).Defaults!;
        var previous = await store.GetHeadAsync(appId, null, cancellationToken).ConfigureAwait(false);
        if ((previous?.MetadataRevision ?? 0) != expectedDefaultsRevision) throw new RegionalMetadataConflictException();
        var diagnostics = RegionalMetadataValidation.Defaults(defaults, snapshot);
        var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), appId, null, expectedDefaultsRevision + 1, null, snapshotId, null, defaults, [], [], null, null,
            Provenance(defaults, previous?.Defaults, previous?.Provenance), diagnostics, null, null, Now);
        return await transactions.ExecuteAsync(ct => store.AppendAsync(revision, previous?.Id, ct), cancellationToken).ConfigureAwait(false);
    }
    public async Task<RegionalMetadataRevision> SaveSequenceAsync(Guid appId, string sequence, long? expectedWorkspaceRevision, string snapshotId,
        RegionalMetadataContent content, IReadOnlyList<RegionalNodeContext>? nodeContexts = null, IReadOnlyList<RegionalDocumentContext>? documentContexts = null,
        CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(appId, sequence, expectedWorkspaceRevision, cancellationToken).ConfigureAwait(false);
        var app = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var previous = await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        return await SaveWithinAsync(app, sequence, mutation, previous, snapshotId, content, nodeContexts ?? previous?.NodeContexts ?? [], documentContexts ?? previous?.DocumentContexts ?? [],
            previous?.InheritedDefaultsId, previous?.InheritedDefaultsDigest, null, null, null, cancellationToken).ConfigureAwait(false);
    }
    public async Task<RegionalMetadataRevision> InheritAsync(Guid appId, string sequence, long? expectedWorkspaceRevision, Guid defaultsRevisionId,
        CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(appId, sequence, expectedWorkspaceRevision, cancellationToken).ConfigureAwait(false);
        var app = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        var source = await GetRevisionAsync(defaultsRevisionId, cancellationToken).ConfigureAwait(false);
        if (source.ApplicationId != appId || source.SequenceNumber is not null || source.Defaults is null) throw new RegionalMetadataScopeException("Defaults inheritance requires this application's exact defaults revision.");
        var previous = await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        if (previous is not null && previous.RegionKey != source.RegionKey) throw new RegionalMetadataScopeException("Defaults and sequence metadata use different regions.");
        var content = Inherit(previous?.Content, source.Defaults);
        var provenance = Provenance(content, previous?.Content, previous?.Provenance).Concat(previous?.Provenance.Where(item => item.FieldPath.StartsWith("nodeContexts", StringComparison.Ordinal) || item.FieldPath.StartsWith("documentContexts", StringComparison.Ordinal)) ?? []).Select(item => InheritedPath(item.FieldPath, source.Defaults)
            ? item with { Origin = "ApplicationDefaults", SourceRevisionId = source.Id, SourceDigest = source.Digest, Status = "Unconfirmed" } : item).ToArray();
        return await SaveWithinAsync(app, sequence, mutation, previous, previous?.StandardsSnapshotId ?? source.StandardsSnapshotId, content, previous?.NodeContexts ?? [], previous?.DocumentContexts ?? [],
            source.Id, source.Digest, provenance, null, null, cancellationToken).ConfigureAwait(false);
    }
    public async Task<RegionalMetadataRevision> CopyNodeContextAsync(Guid appId, string sequence, long? expectedWorkspaceRevision, Guid sourceRevisionId,
        Guid nodeId, IReadOnlyList<Guid> placementIds, CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(appId, sequence, expectedWorkspaceRevision, cancellationToken).ConfigureAwait(false);
        var app = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false); var source = await GetRevisionAsync(sourceRevisionId, cancellationToken).ConfigureAwait(false);
        if (source.ApplicationId != appId || source.SequenceNumber != sequence) throw new RegionalMetadataScopeException("Node defaults must belong to this exact sequence revision.");
        var node = source.NodeContexts.SingleOrDefault(item => item.NodeInstanceId == nodeId) ?? throw new RegionalMetadataScopeException("The source revision has no context for this node.");
        if (placementIds.Count is < 1 or > 1000 || placementIds.Distinct().Count() != placementIds.Count) throw new ArgumentException("Choose distinct bounded placement identities.", nameof(placementIds));
        var previous = await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false) ?? throw new RegionalMetadataScopeException("The sequence has no regional metadata to receive node defaults.");
        var replacements = placementIds.ToDictionary(id => id, id => new RegionalDocumentContext(id, nodeId, node.Country, node.Language, node.ProductInformationType, node.FormType));
        var documents = previous.DocumentContexts.Select(item => replacements.GetValueOrDefault(item.PlacementId) ?? item)
            .Concat(placementIds.Where(id => previous.DocumentContexts.All(item => item.PlacementId != id)).Select(id => replacements[id])).ToArray();
        var evidence = previous.Provenance.ToList();
        for (var index = 0; index < documents.Length; index++)
            if (placementIds.Contains(documents[index].PlacementId)) foreach (var field in new[] { "country", "language", "productInformationType", "formType" })
            { var path = $"documentContexts[{index}].{field}"; evidence.RemoveAll(item => item.FieldPath == path); evidence.Add(new(path, "NodeDefaults", source.Id, source.Digest)); }
        return await SaveWithinAsync(app, sequence, mutation, previous, previous.StandardsSnapshotId, previous.Content!, previous.NodeContexts, documents,
            previous.InheritedDefaultsId, previous.InheritedDefaultsDigest, evidence, null, null, cancellationToken).ConfigureAwait(false);
    }
    public async Task<RegionalMetadataRevision> UpgradeLegacyAsync(Guid appId, string sequence, long? expectedWorkspaceRevision, CancellationToken cancellationToken = default)
    {
        var app = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false); var seq = app.Sequences.Single(item => item.SequenceNumber == sequence);
        var raw = JsonSerializer.Serialize(new { SchemaVersion = 1, ApplicationId = appId, SequenceNumber = sequence, Shared = seq.PublishingMetadata,
            seq.SubmissionType, seq.Description }); using var json = JsonDocument.Parse(raw); var digest = CanonicalJson.Digest(json.RootElement);
        var replay = await store.FindLegacyAsync(appId, sequence, digest, cancellationToken).ConfigureAwait(false); if (replay is not null) { replay.Verify(); return replay; }
        await using var mutation = await mutations.AcquireAsync(appId, sequence, expectedWorkspaceRevision, cancellationToken).ConfigureAwait(false);
        var currentApp = await ApplicationAsync(appId, sequence, cancellationToken).ConfigureAwait(false);
        if (JsonSerializer.Serialize(currentApp.Sequences.Single(item => item.SequenceNumber == sequence).PublishingMetadata) != JsonSerializer.Serialize(seq.PublishingMetadata)) throw new RegionalMetadataConflictException();
        if (await store.GetHeadAsync(appId, sequence, cancellationToken).ConfigureAwait(false) is not null) throw new RegionalMetadataConflictException();
        var snapshot = await snapshots.CaptureAsync(app.EctdTemplateKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        var shared = seq.PublishingMetadata; RegionalMetadataContent content;
        if (Region(app) == "EU") content = new(1, "EU", Eu: new([]));
        else content = new(1, "US", Us: new(null, shared?.ApplicationType, shared?.ApplicantName,
            shared is null ? [] : [new(Guid.NewGuid(), shared.ApplicantContactName, shared.ApplicantContactType,
                [new(Guid.NewGuid(), shared.Telephone, shared.TelephoneNumberType)], [new(Guid.NewGuid(), shared.Email)])],
            shared?.SubmissionType, shared?.SubmissionSubtype, shared?.FormType, shared?.SequenceDescription));
        var evidence = Provenance(content, null, null).Select(item => item with { Origin = "LegacyUnknown" }).ToArray();
        return await SaveWithinAsync(currentApp, sequence, mutation, null, snapshot.Id, content, [], [], null, null, evidence, raw, digest, cancellationToken).ConfigureAwait(false);
    }
    private Task<RegionalMetadataRevision> SaveWithinAsync(SubmissionApplication app, string sequence, WorkspaceMutation mutation, RegionalMetadataRevision? previous,
        string snapshotId, RegionalMetadataContent content, IReadOnlyList<RegionalNodeContext> nodeContexts, IReadOnlyList<RegionalDocumentContext> documentContexts,
        Guid? defaultsId, string? defaultsDigest, IReadOnlyList<RegionalMetadataProvenance>? provenance, string? legacyJson, string? legacyDigest, CancellationToken cancellationToken)
        => SaveWithinAsync(app, sequence, mutation, previous, snapshotId, content, nodeContexts, documentContexts, defaultsId, defaultsDigest, provenance, legacyJson, legacyDigest, null, cancellationToken);
    private async Task<RegionalMetadataRevision> SaveWithinAsync(SubmissionApplication app, string sequence, WorkspaceMutation mutation, RegionalMetadataRevision? previous,
        string snapshotId, RegionalMetadataContent content, IReadOnlyList<RegionalNodeContext> nodeContexts, IReadOnlyList<RegionalDocumentContext> documentContexts,
        Guid? defaultsId, string? defaultsDigest, IReadOnlyList<RegionalMetadataProvenance>? provenance, string? legacyJson, string? legacyDigest,
        SequencePublishingMetadata? shared, CancellationToken cancellationToken)
    {
        RegionalMetadataJson.VerifyBudget(new { content, nodeContexts, documentContexts });
        var structural = RegionalMetadataRevision.Create(Guid.NewGuid(), app.Id, sequence, 1, mutation.Revision + 1,
            snapshotId, content, null, nodeContexts, documentContexts, defaultsId, defaultsDigest, [], [], null, null, Now);
        content = structural.Content!; nodeContexts = structural.NodeContexts; documentContexts = structural.DocumentContexts;
        var snapshot = await SnapshotAsync(app, snapshotId, content.RegionKey, cancellationToken).ConfigureAwait(false);
        var sections = await ScopeAsync(app, sequence, nodeContexts, documentContexts, cancellationToken).ConfigureAwait(false);
        var issues = RegionalMetadataValidation.Sequence(content, snapshot, app.Sequences.Select(item => item.SequenceNumber).ToHashSet(StringComparer.Ordinal), sequence, nodeContexts, documentContexts, sections).ToList();
        if (legacyJson is not null) issues.Add(new("LegacyDefaultsUnconfirmed", "legacy", "Shared stored values are retained as unconfirmed evidence; dynamic regional writer defaults were not materialized."));
        var evidence = provenance ?? Provenance(content, previous?.Content, previous?.Provenance).Concat(
            Provenance(new { NodeContexts = nodeContexts, DocumentContexts = documentContexts }, previous is null ? null : new { previous.NodeContexts, previous.DocumentContexts }, previous?.Provenance)).ToArray();
        var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), app.Id, sequence, (previous?.MetadataRevision ?? 0) + 1, mutation.Revision + 1,
            snapshotId, content, null, nodeContexts, documentContexts, defaultsId, defaultsDigest, evidence, issues, legacyJson, legacyDigest, Now);
        var target = app.Sequences.Single(item => item.SequenceNumber == sequence); var original = target.PublishingMetadata;
        try
        {
            return await mutation.CommitAsync(async ct =>
            {
                if (shared is not null) { target.RevisePublishingMetadata(shared); await applications.UpdateAsync(app, ct).ConfigureAwait(false); }
                return await store.AppendAsync(revision, previous?.Id, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch { if (shared is not null) target.RestorePublishingMetadata(original); throw; }
    }
    private async Task<Dictionary<Guid, string>> ScopeAsync(SubmissionApplication app, string sequence, IReadOnlyList<RegionalNodeContext> nodeContexts,
        IReadOnlyList<RegionalDocumentContext> documentContexts, CancellationToken cancellationToken)
    {
        var workspace = await nodes.GetSequenceAsync(app.Id, sequence, cancellationToken).ConfigureAwait(false); var map = workspace?.Nodes.ToDictionary(node => node.NodeInstanceId) ?? [];
        var sections = new Dictionary<Guid, string>();
        foreach (var context in nodeContexts)
        {
            if (!map.TryGetValue(context.NodeInstanceId, out var node) || node.ApplicationId != app.Id || node.SequenceNumber != sequence) throw new RegionalMetadataScopeException("A node context is outside the exact application/sequence scope.");
            sections[context.NodeInstanceId] = node.CtdSection;
        }
        foreach (var context in documentContexts)
        {
            var placement = await placements.GetAsync(context.PlacementId, cancellationToken).ConfigureAwait(false);
            if (placement is null || placement.ApplicationId != app.Id || placement.SequenceNumber != sequence || context.NodeInstanceId != placement.NodeInstanceId)
                throw new RegionalMetadataScopeException("A document context must match the exact placement, application, sequence and bound node.");
            if (placement.NodeInstanceId is { } id && (!map.TryGetValue(id, out var node) || node.ApplicationId != app.Id || node.SequenceNumber != sequence)) throw new RegionalMetadataScopeException("The placement's node is outside its sequence.");
            sections[context.PlacementId] = placement.CtdSection;
        }
        return sections;
    }
    private async Task<StandardsProfileSnapshot> SnapshotAsync(SubmissionApplication app, string id, string region, CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (snapshot.TemplateKey != app.EctdTemplateKey || region != Region(app)) throw new RegionalMetadataScopeException("The typed region and exact standards snapshot must match the actual application template.");
        return snapshot;
    }
    private async Task<SubmissionApplication> ApplicationAsync(Guid appId, string? sequence, CancellationToken cancellationToken)
    {
        var app = await applications.GetAsync(appId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Application was not found.");
        if (sequence is not null && app.Sequences.All(item => item.SequenceNumber != sequence)) throw new KeyNotFoundException("Sequence was not found."); return app;
    }
    private static string Region(SubmissionApplication app) => app.EctdTemplateKey == "eu-ectd-3.2.2" ? "EU" : app.EctdTemplateKey == "us-fda-ectd-3.2.2" ? "US" : throw new RegionalMetadataScopeException("Unsupported regional template.");
    private static RegionalMetadataContent Inherit(RegionalMetadataContent? previous, RegionalApplicationDefaultsContent defaults)
    {
        if (defaults.Us is { } us)
        {
            var old = previous?.Us ?? new(null, null, null, [], null, null, null, null);
            return new(1, "US", Us: old with { ApplicationIdentifier = us.ApplicationIdentifier ?? old.ApplicationIdentifier, ApplicationType = us.ApplicationType ?? old.ApplicationType,
                ApplicantName = us.ApplicantName ?? old.ApplicantName, Contacts = us.Contacts.Count == 0 ? old.Contacts : us.Contacts });
        }
        var eu = defaults.Eu!;
        return new(1, "EU", Eu: new((previous?.Eu?.Envelopes ?? []).Select(envelope => envelope with
        { Applicant = eu.Applicant ?? envelope.Applicant, ProcedureTrackingNumbers = eu.ProcedureTrackingNumbers.Count == 0 ? envelope.ProcedureTrackingNumbers : eu.ProcedureTrackingNumbers,
            InventedNames = eu.InventedNames.Count == 0 ? envelope.InventedNames : eu.InventedNames, Inns = eu.Inns.Count == 0 ? envelope.Inns : eu.Inns }).ToArray()));
    }
    private static bool InheritedPath(string path, RegionalApplicationDefaultsContent defaults) => defaults.Us is { } us
        ? (us.ApplicationIdentifier is not null && path == "us.applicationIdentifier") || (us.ApplicationType is not null && path == "us.applicationType") ||
          (us.ApplicantName is not null && path == "us.applicantName") || (us.Contacts.Count > 0 && path.StartsWith("us.contacts", StringComparison.Ordinal))
        : (defaults.Eu!.Applicant is not null && path.EndsWith(".applicant", StringComparison.Ordinal)) ||
          (defaults.Eu.ProcedureTrackingNumbers.Count > 0 && path.Contains(".procedureTrackingNumbers", StringComparison.Ordinal)) ||
          (defaults.Eu.InventedNames.Count > 0 && path.Contains(".inventedNames", StringComparison.Ordinal)) || (defaults.Eu.Inns.Count > 0 && path.Contains(".inns", StringComparison.Ordinal));
    private static RegionalMetadataProvenance[] Provenance<T>(T current, T? previous, IReadOnlyList<RegionalMetadataProvenance>? previousProvenance)
    {
        var fields = Fields(JsonSerializer.SerializeToElement(current, JsonOptions));
        var old = previous is null ? new Dictionary<string, FieldValue>() : Fields(JsonSerializer.SerializeToElement(previous, JsonOptions));
        var identity = old.ToDictionary(pair => pair.Value.Identity, pair => pair, StringComparer.Ordinal);
        var evidence = previousProvenance?.GroupBy(item => item.FieldPath).ToDictionary(group => group.Key, group => group.Last()) ?? [];
        return fields.Select(pair => identity.TryGetValue(pair.Value.Identity, out var original) && original.Value.Value == pair.Value.Value && evidence.TryGetValue(original.Key, out var item)
            ? item with { FieldPath = pair.Key } : new RegionalMetadataProvenance(pair.Key, "UserEntered")).ToArray();
    }
    private sealed record FieldValue(string Value, string Identity);
    private static Dictionary<string, FieldValue> Fields(JsonElement element)
    {
        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        void Visit(JsonElement value, string path, string identity)
        {
            if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject()) Visit(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, identity + "." + property.Name);
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    string? id = null;
                    if (item.ValueKind == JsonValueKind.Object) foreach (var key in new[] { "id", "placementId", "nodeInstanceId" }) if (item.TryGetProperty(key, out var found) && found.ValueKind == JsonValueKind.String) { id = found.GetString(); break; }
                    Visit(item, path + "[" + index + "]", identity + "[" + (id ?? index.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "]"); index++;
                }
            }
            else fields[path] = new(value.GetRawText(), identity);
        }
        Visit(element, "", ""); return fields;
    }
}
