using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaSchemaValidityRule
{
    internal const string RuleId = "FDA47-2002";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The schema validity inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "2002" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 2002 requires captured official criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2015, 6, 15))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 2002.");
        var target = packages.Target;
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest())
            return Unknown("The lifecycle facts are not bound to this captured input and context.");
        var findings = new List<ValidationFinding>();
        var work = target.Limits.MaxXmlNodes;
        var incomplete = false;
        var sequence = target.Manifest.SequenceNumber;
        try
        {
            var matches = new List<PackageXmlInspection>();
            foreach (var inspection in lifecycle.XmlInspections) { Tick(); if (inspection.InputDigest == target.InputDigest) matches.Add(inspection); }
            if (matches.Count != 1) return Unknown("A unique current XML inspection is required.");
            var xml = matches[0];
            if (xml.ProfileSnapshotId != lifecycle.ProfileSnapshotId || xml.RulesDigest != context.Catalog.Digest ||
                xml.AssetManifestDigest != context.Assets.Digest || xml.LimitsDigest != target.Limits.Digest())
                return Unknown("XML inspection profile or digest binding is inconsistent.");
            var files = new Dictionary<string, PackageInputFile>(StringComparer.Ordinal);
            foreach (var file in target.Manifest.Files) { Tick(); if (!files.TryAdd(file.LogicalPath, file)) return Unknown("Captured file identities are ambiguous."); }
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var document in xml.Backbones) { Tick(); counts[document.LogicalPath] = counts.GetValueOrDefault(document.LogicalPath) + 1; }
            foreach (var document in xml.StudyTaggingFiles.Documents) { Tick(); counts[document.LogicalPath] = counts.GetValueOrDefault(document.LogicalPath) + 1; }
            incomplete = !xml.StudyTaggingFiles.InventoryComplete || counts.GetValueOrDefault(sequence + "/index.xml") != 1;
            var regionalPaths = new HashSet<string>(StringComparer.Ordinal);
            var regionalProfile = context.Assets.ForProfile(xml.ProfileSnapshotId).SingleOrDefault(item => item.Kind == BackboneKind.UsRegional);
            if (regionalProfile is null) incomplete = true;
            else if (files.ContainsKey(sequence + "/" + regionalProfile.DefaultRelativePath)) regionalPaths.Add(sequence + "/" + regionalProfile.DefaultRelativePath);
            foreach (var index in xml.Backbones)
            {
                Tick();
                if (index.Kind != BackboneKind.Ich || index.LogicalPath != sequence + "/index.xml") continue;
                foreach (var leaf in index.Leaves)
                {
                    Tick();
                    if (!leaf.IsRegionalReference || leaf.Operation == "delete") continue;
                    PackageReference reference;
                    try { reference = PackageLogicalPath.ResolveReference(index.LogicalPath, leaf.Href!); }
                    catch (ArgumentException) { incomplete = true; continue; }
                    if (reference.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null || !path.StartsWith(sequence + "/", StringComparison.Ordinal))
                    { incomplete = true; continue; }
                    regionalPaths.Add(path);
                }
            }
            foreach (var path in regionalPaths)
            {
                Tick();
                var observed = 0;
                foreach (var document in xml.Backbones) { Tick(); if (document.LogicalPath == path && document.Kind == BackboneKind.UsRegional) observed++; }
                if (observed != 1 || counts.GetValueOrDefault(path) != 1 || !files.ContainsKey(path)) incomplete = true;
            }
            var diagnostics = new List<ValidationFinding>();
            foreach (var check in xml.Checks) foreach (var finding in check.Findings) { Tick(); diagnostics.Add(finding); }
            foreach (var document in xml.Backbones)
            {
                Tick();
                var profile = context.Assets.ForProfile(xml.ProfileSnapshotId).SingleOrDefault(item => item.Kind == document.Kind);
                if (profile is null || document.Kind == BackboneKind.EuRegional || document.ProfileSnapshotId != xml.ProfileSnapshotId ||
                    document.Kind == BackboneKind.Ich && document.LogicalPath != sequence + "/index.xml") { incomplete = true; continue; }
                Evaluate(document.LogicalPath, document.Elements, document.ResolvedAssets, profile.MainAssetId, profile.DocumentTypeName,
                    profile.RootLocalName, profile.NamespaceUri, profile.DtdVersion, profile.RuleId, document.ReadComplete, document.DtdStatus, diagnostics);
            }
            foreach (var document in xml.StudyTaggingFiles.Documents)
            {
                Tick();
                if (!files.TryGetValue(document.LogicalPath, out var file) || document.CapturedSha256 != file.Sha256 ||
                    !context.Assets.Assets.TryGetValue("stf-dtd", out var asset) || asset.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f")
                { incomplete = true; continue; }
                Evaluate(document.LogicalPath, document.Elements, document.ResolvedAssets, "stf-dtd", "ectd:study", "study", "http://www.ich.org/ectd", "2.2",
                    "STF-DTD", document.ReadComplete, document.DtdStatus, xml.StudyTaggingFiles.Findings);
            }

            void Evaluate(string path, IReadOnlyList<ParsedXmlElement> elements, IReadOnlyList<string> assets, string mainAsset,
                string rootName, string rootLocal, string rootNamespace, string version, string diagnosticRule, bool complete, CheckStatus status, IEnumerable<ValidationFinding> observations)
            {
                Tick();
                if (!path.StartsWith(sequence + "/", StringComparison.Ordinal) || !files.ContainsKey(path) || counts[path] != 1)
                { incomplete = true; return; }
                var roots = new List<ParsedXmlElement>();
                foreach (var element in elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                var resolved = false;
                foreach (var asset in assets) { Tick(); if (asset == mainAsset) resolved = true; }
                if (roots.Count != 1 || roots[0].Name != rootName || roots[0].LocalName != rootLocal || roots[0].NamespaceUri != rootNamespace ||
                    roots[0].Attribute("dtd-version") != version || roots[0].Location.LogicalPath != path || roots[0].Location.SequenceNumber != sequence || !resolved)
                { incomplete = true; return; }
                ValidationFinding? invalid = null;
                foreach (var diagnostic in observations)
                {
                    Tick();
                    if (diagnostic.Location.LogicalPath != path || diagnostic.Location.SequenceNumber != sequence) continue;
                    if (diagnostic.RuleId == diagnosticRule && diagnostic.CheckStatus == CheckStatus.Fail && diagnostic.Code is "DTD_VALIDATION_ERROR" or "XML_NOT_WELL_FORMED") invalid ??= diagnostic;
                    else if (diagnostic.CheckStatus is CheckStatus.Fail or CheckStatus.NotEvaluated) incomplete = true;
                }
                if (invalid is null) { incomplete |= !complete || status != CheckStatus.Pass; return; }
                if (remaining <= 0) { incomplete = true; return; }
                remaining--;
                findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                    "A captured XML file has a definite diagnostic from its bound DTD/XML reader.",
                    invalid.Location with { Files = [new(target.SourceId, path)], FieldPath = "xml-schema-validity" },
                    Expected: "Conformance to the applicable XML/DTD schema", Actual: invalid.Code));
                retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A schema error is established; other files may remain unevaluated.", findings.ToArray()));
            }
        }
        catch (SchemaWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Checked bound current XML/DTD diagnostic categories only. Attribute-file semantics, historical references, declaration-only identity, discovery classification and interrupted STF observation retention remain unqualified.", findings);
        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new SchemaWorkLimitException(); }
    }
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed class SchemaWorkLimitException : Exception;
}
