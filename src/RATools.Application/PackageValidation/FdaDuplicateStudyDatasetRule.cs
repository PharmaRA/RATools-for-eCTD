using RATools.Application.Standards;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaDuplicateStudyDatasetRule
{
    internal const string RuleId = "FDA47-1737";
    internal static async Task<RuleCheckResult?> InspectAsync(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The study dataset duplicate inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "1737" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("FDA 1737 requires the archived criteria and an explicit selection date.");
        if (context.SelectionDate < new DateOnly(2023, 3, 16))
            return new(RuleId, CheckStatus.NotApplicable, "The selected date precedes the effective date of FDA 1737.");
        if (!context.HasArchivedOfficialSource(BundledOfficialSources.StudyDataTechnicalGuide) ||
            !context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("The captured study-data guide and STF specification are required.");

        var data = await PackageStudyDataInspector.InspectAsync(packages, lifecycle, context, cancellationToken: cancellationToken).ConfigureAwait(false);
        var incomplete = !data.ScopeComplete;
        var applicable = false;
        var studies = 0;
        var findings = new List<ValidationFinding>();
        var work = packages.Target.Limits.MaxXmlNodes;
        var reported = new HashSet<(LeafAddress, LeafAddress)>();
        try
        {
            var xmls = lifecycle.XmlInspections.Where(xml => xml.InputDigest == packages.Target.InputDigest && xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
            if (xmls.Length != 1) return Unknown("A unique frozen target XML inspection is required.");
            var backbones = new Dictionary<string, ParsedBackbone>(StringComparer.Ordinal);
            var elements = new Dictionary<(string Path, string Node), ParsedXmlElement>();
            var children = new Dictionary<(string Path, string Parent), List<ParsedXmlElement>>();
            foreach (var backbone in xmls[0].Backbones)
            {
                Tick();
                if (!backbones.TryAdd(backbone.LogicalPath, backbone)) return Unknown("Ambiguous target backbone facts.");
                foreach (var element in backbone.Elements)
                {
                    Tick();
                    if (!elements.TryAdd((backbone.LogicalPath, element.NodePath), element)) return Unknown("Ambiguous target element facts.");
                    if (element.ParentPath is { } parent)
                    {
                        var key = (backbone.LogicalPath, parent);
                        if (!children.TryGetValue(key, out var members)) children.Add(key, members = []);
                        members.Add(element);
                    }
                }
            }
            foreach (var document in data.Documents)
            {
                Tick();
                var inScope = false;
                var qualified = document.Stf.StructureComplete;
                foreach (var owner in document.Stf.Owners)
                {
                    Tick();
                    if (owner.Leaf.Address?.SequenceNumber != packages.Target.Manifest.SequenceNumber) continue;
                    if (!owner.Qualified || owner.Heading is null || !context.NodeSchema.Definitions.TryGetValue(owner.Heading, out var definition) ||
                        definition.SectionPath is not { } section) { qualified = false; continue; }
                    if (section.StartsWith("m4.", StringComparison.Ordinal) || section.StartsWith("m5.", StringComparison.Ordinal)) inScope = true;
                }
                if (!qualified) { incomplete = true; continue; }
                if (!inScope) continue;
                studies++;
                var seen = new HashSet<LeafAddress>();
                var pairs = new Dictionary<(string Name, string Title), (LeafAddress Address, string Path, string SourceId)>();
                foreach (var reference in document.References)
                {
                    Tick();
                    if (reference.Reference.Status != StfReferenceStatus.Resolved || reference.Reference.Leaf?.Address is not { } address)
                    { incomplete = true; continue; }
                    if (!seen.Add(address)) continue; // Repeating a reference is not another submitted leaf event.
                    var leaf = reference.Reference.Leaf;
                    if (leaf.Operation != "new") continue;
                    if (address.SequenceNumber != packages.Target.Manifest.SequenceNumber) { incomplete = true; continue; }
                    if (reference.Status != StudyDataReferenceStatus.Read || reference.ContentPath is not { } path ||
                        reference.Xpt?.Content is not { Status: XptReadStatus.Read, StructureComplete: true })
                    { incomplete = true; continue; }
                    applicable = true;
                    var backbonePath = address.SequenceNumber + "/" + address.BackboneRelativePath;
                    if (!backbones.TryGetValue(backbonePath, out var backbone) || !backbone.ReadComplete || backbone.DtdStatus != CheckStatus.Pass ||
                        backbone.ProfileSnapshotId != lifecycle.ProfileSnapshotId || backbone.Kind is not (BackboneKind.Ich or BackboneKind.UsRegional) ||
                        !elements.TryGetValue((backbonePath, leaf.NodePath), out var original) || original.LocalName != "leaf" || original.NamespaceUri.Length != 0 ||
                        original.Location.LogicalPath != backbonePath || original.Location.NodePath != original.NodePath ||
                        leaf.Location.LogicalPath != backbonePath || leaf.Location.NodePath != original.NodePath ||
                        Explicit(original, "ID", "") != address.LeafId || Explicit(original, "operation", "") != "new" ||
                        Explicit(original, "href", "http://www.w3c.org/1999/xlink") is not { } href || href != leaf.Href)
                    { incomplete = true; continue; }
                    var titles = new List<ParsedXmlElement>();
                    foreach (var childPath in original.Children)
                    {
                        Tick();
                        if (elements.TryGetValue((backbonePath, childPath), out var child) && child.ParentPath == original.NodePath &&
                            child.LocalName == "title" && child.NamespaceUri.Length == 0) titles.Add(child);
                    }
                    var observedTitles = 0;
                    foreach (var child in children.GetValueOrDefault((backbonePath, original.NodePath)) ?? [])
                    { Tick(); if (child.LocalName == "title" && child.NamespaceUri.Length == 0) observedTitles++; }
                    if (titles.Count != 1 || observedTitles != 1 || titles[0].Children.Count != 0 ||
                        children.ContainsKey((backbonePath, titles[0].NodePath)) || titles[0].Location.LogicalPath != backbonePath ||
                        titles[0].Location.NodePath != titles[0].NodePath)
                    { incomplete = true; continue; }
                    var key = (path.Split('/')[^1], titles[0].Text);
                    if (!pairs.TryGetValue(key, out var previous)) { pairs.Add(key, (address, path, reference.Xpt.SourceId)); continue; }
                    if (!reported.Add((previous.Address, address))) continue;
                    if (remaining <= 0) { incomplete = true; continue; }
                    remaining--;
                    findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                        "Two distinct current new dataset leaves in this STF have the same filename and original leaf title.",
                        titles[0].Location with { LeafId = address.LeafId, FieldPath = "title",
                            Files = [new(packages.Target.SourceId, backbonePath), new(document.Stf.SourceId, document.Stf.Document.LogicalPath),
                                new(previous.SourceId, previous.Path), new(reference.Xpt.SourceId, path)] },
                        Expected: "At most one new dataset leaf for the exact filename/title pair",
                        Actual: previous.Address.SequenceNumber + "/" + previous.Address.BackboneRelativePath + "#" + previous.Address.LeafId +
                            " and " + backbonePath + "#" + address.LeafId + "; filename=" + key.Item1 + "; title=" + key.Item2));
                    retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A duplicate new dataset pair is established; other study scope may remain unqualified.", findings.ToArray()));
                }
            }
            // This implementation does not merge separate STF representations or count historical lifecycle states.
            if (studies > 1) incomplete = true;
        }
        catch (DuplicateStudyWorkLimitException) { incomplete = true; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : applicable ? CheckStatus.Pass : CheckStatus.NotApplicable,
            "Compared distinct current new XPT dataset leaves within each qualified STF using exact decoded filenames and untrimmed leaf titles. Cross-STF study identity, historical lifecycle counting and other dataset formats remain Partial.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new DuplicateStudyWorkLimitException(); }
        string? Explicit(ParsedXmlElement element, string name, string ns)
        {
            var count = 0; string? value = null;
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
    private static RuleCheckResult Unknown(string reason) => new(RuleId, CheckStatus.NotEvaluated, reason);
    private sealed class DuplicateStudyWorkLimitException : Exception;
}
