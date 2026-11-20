using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfCrossReferenceRule
{
    internal const string RuleId = "FDA47-1799";
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The STF cross-reference inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1799" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1799 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2008, 3, 10))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1799.");

        var bindings = PackageStfBindingReader.Read(packages, lifecycle, context, cancellationToken);
        var incomplete = !bindings.TargetScopeComplete;
        var applicable = false;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        try
        {
            var observations = new Dictionary<(string Digest, string Path), int>();
            var xmlByDigest = new Dictionary<string, List<PackageXmlInspection>>(StringComparer.Ordinal);
            foreach (var xml in lifecycle.XmlInspections)
            {
                Tick();
                if (!xmlByDigest.TryGetValue(xml.InputDigest, out var matches)) xmlByDigest.Add(xml.InputDigest, matches = []);
                matches.Add(xml);
                foreach (var document in xml.StudyTaggingFiles.Documents)
                {
                    Tick();
                    var key = (xml.InputDigest, document.LogicalPath);
                    observations[key] = observations.GetValueOrDefault(key) + 1;
                }
            }
            var stfs = new Dictionary<string, List<BoundStudyTaggingFile>>(StringComparer.Ordinal);
            foreach (var document in bindings.Documents)
            {
                Tick();
                if (!stfs.TryGetValue(document.Document.LogicalPath, out var matches)) stfs.Add(document.Document.LogicalPath, matches = []);
                matches.Add(document);
            }
            foreach (var bound in bindings.Documents)
            {
                Tick();
                var selected = bound.IsTarget;
                foreach (var owner in bound.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != packages.Target.Manifest.SequenceNumber) continue;
                    if (owner.Qualified) selected = true;
                    else incomplete = true;
                }
                if (!selected) continue;
                applicable = true;
                if (!ActualStf(bound)) { incomplete = true; continue; }
                incomplete |= !bound.StructureComplete || !bound.Document.ReadComplete || bound.Document.DtdStatus != CheckStatus.Pass;
                foreach (var reference in bound.References)
                {
                    Tick();
                    if (reference.Status != StfReferenceStatus.Resolved || reference.Leaf?.Address is not { } address)
                    { incomplete = true; continue; }
                    var href = QualifiedContentHref(reference.Leaf);
                    if (href is null) { incomplete = true; continue; }
                    PackageReference content;
                    try { content = PackageLogicalPath.ResolveReference(address.SequenceNumber + "/" + address.BackboneRelativePath, href); }
                    catch (ArgumentException) { incomplete = true; continue; }
                    if (content.LogicalPath is not { } path || content.Query is not null || content.Fragment is not null ||
                        string.CompareOrdinal(path.Split('/')[0], address.SequenceNumber) > 0)
                    { incomplete = true; continue; }
                    if (!packages.Sequences.TryGetValue(path.Split('/')[0], out var capture)) { incomplete = true; continue; }
                    var files = 0;
                    foreach (var file in capture.Manifest.Files) { Tick(); if (file.LogicalPath == path) files++; }
                    if (files != 1) { incomplete = true; continue; }
                    if (!stfs.TryGetValue(path, out var targets))
                    {
                        // Absence from the STF list proves a negative role only for a file
                        // actually included in a complete, correctly bound XML discovery scope.
                        if (!ProvenNonStf(capture, path)) incomplete = true;
                        continue;
                    }
                    if (targets.Count != 1 || !ActualStf(targets[0])) { incomplete = true; continue; }
                    if (path == bound.Document.LogicalPath)
                    { incomplete = true; continue; } // The archived "another STF" wording does not settle self-reference.
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "This STF doc-content resolves through a backbone leaf to another actual STF file.",
                        reference.Element.Location with { FieldPath = "doc-content/@xlink:href", Files =
                            [new(bound.SourceId, bound.Document.LogicalPath), new(targets[0].SourceId, path)] },
                        Expected: "A referenced content file that is not another STF", Actual: path));
                    retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A reference to another STF is established; other references may remain unqualified.", findings.ToArray()));
                }
            }

            string? QualifiedContentHref(ParsedPackageLeaf leaf)
            {
                Tick();
                if (leaf.Address is not { } address || !packages.Sequences.TryGetValue(address.SequenceNumber, out var capture) ||
                    !xmlByDigest.TryGetValue(capture.InputDigest, out var matches) || matches.Count != 1) return null;
                var backbonePath = address.SequenceNumber + "/" + address.BackboneRelativePath;
                var documents = new List<ParsedBackbone>();
                foreach (var document in matches[0].Backbones) { Tick(); if (document.LogicalPath == backbonePath) documents.Add(document); }
                if (documents.Count != 1) return null;
                var elements = new List<ParsedXmlElement>();
                foreach (var element in documents[0].Elements) { Tick(); if (element.NodePath == leaf.NodePath) elements.Add(element); }
                if (elements.Count != 1 || elements[0] is not { Name: "leaf", LocalName: "leaf", NamespaceUri.Length: 0 }) return null;
                ParsedXmlAttributeValue? href = null;
                foreach (var attribute in elements[0].Attributes.Values)
                {
                    Tick();
                    if (attribute.LocalName == "href" && attribute.NamespaceUri == "http://www.w3c.org/1999/xlink") href = attribute;
                }
                return href is { IsDefault: false } && href.Value == leaf.Href ? href.Value : null;
            }

            bool ActualStf(BoundStudyTaggingFile bound)
            {
                if (observations.GetValueOrDefault((bound.InputDigest, bound.Document.LogicalPath)) != 1) return false;
                var roots = new List<ParsedXmlElement>();
                foreach (var element in bound.Document.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                return roots.Count == 1 && roots[0] is { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" };
            }

            bool ProvenNonStf(ICapturedPackageInput capture, string path)
            {
                Tick();
                var number = capture.Manifest.SequenceNumber;
                var selected = context.ForSequence(number);
                var profile = number == packages.Target.Manifest.SequenceNumber ? lifecycle.ProfileSnapshotId :
                    packages.Baseline.Entries.Single(entry => entry.SequenceNumber == number).ProfileSnapshotId;
                if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || path.StartsWith(number + "/util/", StringComparison.Ordinal) ||
                    !xmlByDigest.TryGetValue(capture.InputDigest, out var matches) || matches.Count != 1) return false;
                var xml = matches[0];
                if (xml.ProfileSnapshotId != profile || xml.RulesDigest != selected.Catalog.Digest || xml.AssetManifestDigest != selected.Assets.Digest ||
                    xml.LimitsDigest != capture.Limits.Digest() || !xml.StudyTaggingFiles.InventoryComplete) return false;
                foreach (var document in xml.Backbones) { Tick(); if (document.LogicalPath == path) return false; }
                foreach (var document in xml.StudyTaggingFiles.Documents) { Tick(); if (document.LogicalPath == path) return false; }
                return true;
            }
        }
        catch (CrossReferenceWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Checked actual target STFs and qualified current-owner historical sources through exact leaf/content references. Retained root and reference facts can establish another STF despite later tail damage. Self-reference interpretation and content identities outside completed XML discovery remain unqualified; local passes do not establish full FDA 1799 coverage.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new CrossReferenceWorkLimitException(); }
    }
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class CrossReferenceWorkLimitException : Exception;
}
