using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

// A reference association is not a main-form classification or a choice of application.
public sealed class PackageFormEvidenceAssociation(string sourceLogicalPath, string groupNodePath, string leafNodePath,
    string? leafId, string scope, ParsedXmlAttributeValue? declaredFormType,
    IEnumerable<PackageFormApplication> candidateApplications, PackageFormFieldEvidence fields)
{
    public string SourceLogicalPath { get; } = sourceLogicalPath;
    public string GroupNodePath { get; } = groupNodePath;
    public string LeafNodePath { get; } = leafNodePath;
    public string? LeafId { get; } = leafId;
    public string Scope { get; } = scope;
    public ParsedXmlAttributeValue? DeclaredFormType { get; } = declaredFormType;
    public IReadOnlyList<PackageFormApplication> CandidateApplications { get; } = Array.AsReadOnly(candidateApplications.ToArray());
    public PackageFormFieldEvidence Fields { get; } = fields;
    public string AlgorithmVersion { get; } = "form-leaf-evidence-association-v1";
}

internal static class PackageFormEvidenceAssociationReader
{
    internal static IReadOnlyList<PackageFormEvidenceAssociation> Read(PackageDeliveryInput input, PackageFileInspection files,
        PackagePdfInspection pdf, PackageStandardsContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!files.Forms.InventoryComplete || context.SnapshotId is null || files.InputDigest != input.InputDigest ||
            files.HistoryManifestDigest != input.Packages.Baseline.Digest || files.RulesDigest != context.Catalog.Digest ||
            files.AssetManifestDigest != context.Assets.Digest || files.LimitsDigest != input.Packages.Target.Limits.Digest() ||
            pdf.InputDigest != files.InputDigest || pdf.HistoryManifestDigest != files.HistoryManifestDigest ||
            pdf.RulesDigest != files.RulesDigest || pdf.ProfileSnapshotId != files.ProfileSnapshotId || pdf.LimitsDigest != files.LimitsDigest) return [];
        var work = input.Packages.Target.Limits.MaxXmlNodes;
        var captured = new Dictionary<string, PackageInputFile>(StringComparer.Ordinal);
        var documents = new Dictionary<string, PackagePdfDocument>(StringComparer.Ordinal);
        var templates = new Dictionary<string, PackageFormTemplateMatch>(StringComparer.Ordinal);
        var fields = new Dictionary<string, PackageFormFieldEvidence>(StringComparer.Ordinal);
        var references = new Dictionary<(string Source, string Node), PackageFileReference?>();
        var owners = new HashSet<(string Source, string Node)>();
        var groups = new HashSet<(string Source, string Node)>();
        var result = new List<PackageFormEvidenceAssociation>();
        var prefix = input.Packages.Target.Manifest.SequenceNumber + "/";
        foreach (var file in input.Packages.Target.Manifest.Files)
            if (!Tick() || !captured.TryAdd(file.LogicalPath, file)) return [];
        foreach (var document in pdf.Documents)
            if (!Tick() || !documents.TryAdd(document.LogicalPath, document)) return [];
        foreach (var template in pdf.FormTemplates)
            if (!Tick() || !templates.TryAdd(template.LogicalPath, template)) return [];
        foreach (var field in pdf.FormFieldEvidence)
            if (!Tick() || !fields.TryAdd(field.Template.LogicalPath, field)) return [];
        foreach (var reference in files.References)
        {
            if (!Tick()) return [];
            if (reference.Kind != "Leaf" || reference.Location.NodePath is not { } node) continue;
            var key = (reference.SourceLogicalPath, node);
            if (!references.TryAdd(key, reference)) references[key] = null;
        }
        // Complete ownership indexing precedes publication, so later duplicates cannot
        // leave an earlier association pretending to have unique group ownership.
        foreach (var group in files.Forms.Groups)
        {
            if (!Tick() || !groups.Add((group.SourceLogicalPath, group.NodePath))) return [];
            foreach (var leaf in group.Leaves)
                if (!Tick() || !owners.Add((group.SourceLogicalPath, leaf.NodePath))) return [];
        }
        // One bounded replay for the entire association pass, never one replay per leaf.
        // It reads already captured facts only and keeps its own aggregate PDF-object budget.
        var replay = new FdaXfaTemplateEvidence.Inspection(input, files, context, token);
        foreach (var document in documents.Values)
        {
            if (!Tick()) return [];
            if (fields.ContainsKey(document.LogicalPath)) replay.Observe(document);
        }
        var verifiedTemplates = new Dictionary<string, PackageFormTemplateMatch>(StringComparer.Ordinal);
        var verifiedFields = new Dictionary<string, PackageFormFieldEvidence>(StringComparer.Ordinal);
        foreach (var template in replay.Matches)
            if (!Tick() || !verifiedTemplates.TryAdd(template.LogicalPath, template)) return [];
        foreach (var field in replay.Fields)
            if (!Tick() || !verifiedFields.TryAdd(field.Template.LogicalPath, field)) return [];
        foreach (var group in files.Forms.Groups)
        {
            if (!Tick()) return [];
            if (!group.XmlReadComplete || group.DtdStatus != CheckStatus.Pass ||
                group.Scope is not ("ApplicationAdmin" or "SharedM1") || !group.SourceLogicalPath.StartsWith(prefix, StringComparison.Ordinal) ||
                !captured.ContainsKey(group.SourceLogicalPath)) continue;
            // Copy all candidate applications, including non-storage applications and
            // raw duplicate/unknown numbers; the association does not resolve ownership.
            foreach (var application in group.Applications)
            {
                if (!Tick()) return [];
                foreach (var number in application.Numbers) if (!Tick()) return [];
            }
            foreach (var leaf in group.Leaves)
            {
                if (!Tick()) return [];
                if (leaf.Operation is not ("new" or "replace" or "append") || string.IsNullOrWhiteSpace(leaf.Href) ||
                    !leaf.NodePath.StartsWith(group.NodePath + "/", StringComparison.Ordinal) ||
                    leaf.Location.LogicalPath != group.SourceLogicalPath || leaf.Location.NodePath != leaf.NodePath ||
                    leaf.Location.LeafId != leaf.Id ||
                    !references.TryGetValue((group.SourceLogicalPath, leaf.NodePath), out var reference) || reference is null ||
                    !reference.Exists || reference.Reference != leaf.Href || reference.Location.LogicalPath != group.SourceLogicalPath ||
                    reference.Location.LeafId != leaf.Id || reference.Resolved is not { LogicalPath: { } path, Query: null, Fragment: null, ExternalUri: null } ||
                    !path.StartsWith(prefix, StringComparison.Ordinal) || !captured.TryGetValue(path, out var file)) continue;
                PackageReference resolved;
                try { resolved = PackageLogicalPath.ResolveReference(group.SourceLogicalPath, leaf.Href); }
                catch (ArgumentException) { continue; }
                if (resolved != reference.Resolved || !documents.TryGetValue(path, out var document) || document.CapturedSha256 != file.Sha256 ||
                    !templates.TryGetValue(path, out var template) || template.CapturedSha256 != file.Sha256 || template.StandardsSnapshotId != context.SnapshotId ||
                    !fields.TryGetValue(path, out var field) || field.Template != template || field.AlgorithmVersion != Fda2252FieldEvidence.AlgorithmVersion) continue;
                // Revalidate typed evidence against the capture-bound document facts,
                // not merely a caller-provided matching path or SHA string.
                if (!verifiedTemplates.TryGetValue(path, out var verifiedTemplate) || verifiedTemplate != template ||
                    !verifiedFields.TryGetValue(path, out var verifiedField) || verifiedField != field) continue;
                // Each association freezes its own application list. Charge that copy
                // per leaf, even when several leaves share the same candidate group.
                foreach (var application in group.Applications) if (!Tick()) return [];
                result.Add(new(group.SourceLogicalPath, group.NodePath, leaf.NodePath, leaf.Id, group.Scope, group.FormType, group.Applications, field));
            }
        }
        token.ThrowIfCancellationRequested();
        return Array.AsReadOnly(result.ToArray());

        bool Tick() { token.ThrowIfCancellationRequested(); return work-- > 0; }
    }
}
