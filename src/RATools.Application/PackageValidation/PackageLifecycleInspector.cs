using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed class PackageLifecycleInspector(TimeProvider? timeProvider = null)
{
    private static readonly string[] RuleIds = ["HISTORY-BASELINE", "LIFECYCLE-OPERATION", "LIFECYCLE-URI",
        "LIFECYCLE-TARGET", "LIFECYCLE-EFFECTIVE", "LIFECYCLE-CONTEXT"];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<PackageLifecycleInspection> InspectAsync(PackageInputSet inputs, string profileSnapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        _ = PackageXmlAssets.Current.ForProfile(profileSnapshotId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(inputs.Target.Limits.MaxElapsedSeconds), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var run = new Inspection(inputs, profileSnapshotId, linked.Token);
        try
        {
            await inputs.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
            foreach (var (sequence, input) in inputs.Sequences.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                linked.Token.ThrowIfCancellationRequested();
                var entry = inputs.Baseline.Entries.SingleOrDefault(entry => entry.SequenceNumber == sequence);
                var profile = entry?.ProfileSnapshotId ?? profileSnapshotId;
                if (entry is not null)
                {
                    if (entry.TrustStatus != HistoryTrustStatus.VerifiedForScope)
                        run.Add("HISTORY-BASELINE", entry.TrustStatus == HistoryTrustStatus.Rejected ? CheckStatus.Fail : CheckStatus.NotEvaluated,
                            "HISTORY_TRUST_UNQUALIFIED", "Historical bytes are selected, but their trust is not verified for this scope.", new(sequence));
                    if (profile != profileSnapshotId)
                        run.Add("HISTORY-BASELINE", CheckStatus.NotEvaluated, "HISTORY_PROFILE_COMPATIBILITY_UNQUALIFIED",
                            "Cross-profile lifecycle compatibility requires an explicit qualified policy.", new(sequence));
                }
                try { _ = PackageXmlAssets.Current.ForProfile(profile); }
                catch (ArgumentException)
                {
                    run.Incomplete = true;
                    run.Add("HISTORY-BASELINE", CheckStatus.NotEvaluated, "HISTORY_PROFILE_UNSUPPORTED",
                        "The selected historical XML profile is not supported.", new(sequence));
                    continue;
                }
                var xml = await new PackageXmlInspector(_timeProvider).InspectAsync(input, profile, linked.Token).ConfigureAwait(false);
                run.Xml.Add(xml);
                run.CheckLimits();
            }
            run.Resolve();
            await inputs.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
        }
        catch (LifecycleLimitException)
        {
            run.Incomplete = true;
            run.AddTerminal("LIFECYCLE_LIMIT", "Lifecycle inspection exceeded the aggregate backbone, node or finding limit.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            run.Incomplete = true;
            run.AddTerminal("LIFECYCLE_TIMEOUT", "Lifecycle inspection exceeded its configured deadline.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return run.Finish();
    }

    private sealed class LifecycleLimitException : Exception;

    private sealed class Inspection(PackageInputSet inputs, string profile, CancellationToken cancellationToken)
    {
        public List<PackageXmlInspection> Xml { get; } = [];
        public bool Incomplete { get; set; }
        private readonly List<ValidationFinding> _findings = [];
        private readonly List<Event> _events = [];
        private readonly Dictionary<LeafAddress, PackageLeafState> _states = [];
        private readonly HashSet<LeafAddress> _appendMembers = [];
        private readonly HashSet<LeafAddress> _unresolvedTargets = [];
        private readonly HashSet<string> _uncertainSequences = new(StringComparer.Ordinal);
        private Dictionary<string, ParsedBackbone> _documents = new(StringComparer.Ordinal);
        private Dictionary<string, HashSet<string>> _files = new(StringComparer.Ordinal);

        public void Add(string rule, CheckStatus status, string code, string message, ValidationLocation location,
            Event? item = null, string? actual = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_findings.Count >= inputs.Target.Limits.MaxXmlFindings - 1) throw new LifecycleLimitException();
            var finding = new ValidationFinding(rule, status, status == CheckStatus.Fail ? ValidationSeverity.Error : ValidationSeverity.Warning,
                code, message, location, Actual: actual);
            _findings.Add(finding);
            item?.Findings.Add(finding);
        }

        public void AddTerminal(string code, string message) => _findings.Add(new("LIFECYCLE-EFFECTIVE", CheckStatus.NotEvaluated,
            ValidationSeverity.Error, code, message, new(inputs.Target.Manifest.SequenceNumber)));

        public void CheckLimits()
        {
            if (Xml.Sum(xml => (long)xml.Backbones.Count) > inputs.Target.Limits.MaxBackbones ||
                Xml.Sum(xml => xml.Backbones.Sum(document => (long)document.Elements.Count)) > inputs.Target.Limits.MaxXmlNodes)
                throw new LifecycleLimitException();
        }

        public void Resolve()
        {
            foreach (var xml in Xml.Where(xml => xml.Checks.Any(check => check.Status == CheckStatus.Fail ||
                         check.Status == CheckStatus.NotEvaluated && check.RuleId is "PROFILE-VERSION" or "XML-IDS" or "XML-LIMITS")))
            {
                Incomplete = true;
                foreach (var document in xml.Backbones) _uncertainSequences.Add(document.LogicalPath.Split('/')[0]);
            }
            _documents = Xml.SelectMany(xml => xml.Backbones).ToDictionary(document => document.LogicalPath, StringComparer.Ordinal);
            _files = inputs.Sequences.ToDictionary(pair => pair.Key,
                pair => pair.Value.Manifest.Files.Select(file => file.LogicalPath).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
            foreach (var sequence in inputs.Sequences.Keys)
                if (!_documents.TryGetValue(sequence + "/index.xml", out var index) || !index.ReadComplete ||
                    _documents.Values.Any(document => document.LogicalPath.StartsWith(sequence + "/", StringComparison.Ordinal) &&
                        (!document.ReadComplete || document.DtdStatus != CheckStatus.Pass)))
                {
                    Incomplete = true;
                    _uncertainSequences.Add(sequence);
                    Add("LIFECYCLE-EFFECTIVE", CheckStatus.NotEvaluated, "HISTORY_XML_INCOMPLETE",
                        "Incomplete or invalid XML prevents a complete lifecycle replay.", new(sequence));
                }

            foreach (var group in _documents.Values.OrderBy(document => document.LogicalPath, StringComparer.Ordinal)
                         .GroupBy(document => document.LogicalPath.Split('/')[0]))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = group.SelectMany(document => document.Leaves.Select(leaf => new Event(document, leaf)))
                    .OrderBy(item => item.Document.LogicalPath, StringComparer.Ordinal)
                    .ThenBy(item => item.Leaf.Id, StringComparer.Ordinal).ThenBy(item => item.Leaf.NodePath, StringComparer.Ordinal).ToArray();
                // Resolve every reference against immutable XML indexes before changing effectiveness.
                foreach (var item in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _events.Add(item);
                    ValidateOperation(item);
                    if (!string.IsNullOrEmpty(item.Leaf.ModifiedFile)) ResolveTarget(item);
                    if (item.Leaf.Operation is "replace" or "append" or "delete" && item.TargetLeaf is null)
                        foreach (var rule in new[] { "LIFECYCLE-TARGET", "LIFECYCLE-EFFECTIVE", "LIFECYCLE-CONTEXT" })
                            Issue(item, rule, CheckStatus.NotEvaluated, "TARGET_PREREQUISITE_UNRESOLVED", "The exact target could not be established; dependent checks are incomplete.");
                    if (item.Leaf.Address is { } address)
                        _states.TryAdd(address, new(address, PackageLeafEffectiveness.Unknown, item.Leaf.IsRegionalReference));
                }
                foreach (var conflicts in batch.Where(item => item.Target is not null && item.Leaf.Operation != "new")
                             .GroupBy(item => item.Target!).Where(items => items.Count() > 1))
                    foreach (var item in conflicts)
                        Issue(item, "LIFECYCLE-EFFECTIVE", CheckStatus.NotEvaluated, "SAME_SEQUENCE_CONFLICT",
                            "Multiple operations target this leaf in one sequence; no order-dependent winner is selected.");

                foreach (var item in batch.Where(item => item.TargetLeaf is not null && item.Leaf.Operation != "new"))
                    CheckEffectiveness(item);
                // An unresolved operation can change what later sequences consider current.
                // Preserve that uncertainty instead of silently dropping the operation.
                foreach (var item in batch.Where(item => item.Status != CheckStatus.Pass))
                {
                    if (item.Target is { } unresolvedTarget) _unresolvedTargets.Add(unresolvedTarget);
                    if (item.Target is { } target && _states.TryGetValue(target, out var state) &&
                        state.Effectiveness is PackageLeafEffectiveness.Current or PackageLeafEffectiveness.Unknown)
                        _states[target] = state with { Effectiveness = PackageLeafEffectiveness.Unknown };
                    if (item.Leaf.Operation != "new" && item.Target is null) _uncertainSequences.Add(group.Key);
                }
                foreach (var item in batch.Where(item => item.Status == CheckStatus.Pass)) Apply(item);
            }
        }

        private void ValidateOperation(Event item)
        {
            var leaf = item.Leaf;
            if (!item.Document.ReadComplete || item.Document.DtdStatus != CheckStatus.Pass || leaf.Address is null ||
                !item.Document.IdIndex.TryGetValue(leaf.Id ?? "", out var ids) || ids.Count != 1)
                Issue(item, "LIFECYCLE-OPERATION", CheckStatus.NotEvaluated, "SOURCE_XML_UNUSABLE",
                    "The source leaf requires complete, DTD-valid XML and an unambiguous leaf ID.");
            if (leaf.Operation is not ("new" or "replace" or "append" or "delete"))
                Issue(item, "LIFECYCLE-OPERATION", CheckStatus.Fail, "INVALID_OPERATION", "Expected new, replace, append or delete.");
            if (leaf.IsRegionalReference && leaf.Operation != "new")
                Issue(item, "LIFECYCLE-OPERATION", CheckStatus.Fail, "REGIONAL_REFERENCE_MUST_BE_NEW",
                    "ICH Appendix 6 requires the leaf referencing the regional backbone to use operation new.");
            if (leaf.Operation == "new" && !string.IsNullOrEmpty(leaf.ModifiedFile))
                Issue(item, "LIFECYCLE-OPERATION", CheckStatus.Fail, "NEW_HAS_MODIFIED_FILE", "A new leaf is independent of previous leaves.");
            if (leaf.Operation is "replace" or "append" or "delete" && string.IsNullOrEmpty(leaf.ModifiedFile))
                Issue(item, "LIFECYCLE-URI", CheckStatus.Fail, "MODIFIED_FILE_REQUIRED", "This operation requires an exact modified-file target.");
            if (leaf.Operation == "delete")
            {
                if (!string.IsNullOrEmpty(leaf.Href))
                    Issue(item, "LIFECYCLE-OPERATION", CheckStatus.Fail, "DELETE_HAS_CONTENT", "Delete delivers no new file.");
                if (leaf.Checksum != "")
                    Issue(item, "LIFECYCLE-OPERATION", CheckStatus.Fail, "DELETE_CHECKSUM_NOT_EMPTY", "Delete requires checksum=\"\".");
            }
            else if (string.IsNullOrWhiteSpace(leaf.Href))
                Issue(item, "LIFECYCLE-OPERATION", CheckStatus.Fail, "CONTENT_REFERENCE_REQUIRED", "A content leaf requires an href.");
        }

        private void ResolveTarget(Event item)
        {
            PackageReference reference;
            try { reference = PackageLogicalPath.ResolveReference(item.Document.LogicalPath, item.Leaf.ModifiedFile!); }
            catch (PackageReferenceScopeException)
            {
                Issue(item, "LIFECYCLE-URI", CheckStatus.Fail, "TARGET_OUTSIDE_APPLICATION", "The reference escapes the selected application's root.");
                return;
            }
            catch (ArgumentException)
            {
                Issue(item, "LIFECYCLE-URI", CheckStatus.Fail, "INVALID_MODIFIED_FILE", "The reference is malformed or escapes the application.");
                return;
            }
            if (reference.LogicalPath is not { } path || reference.Query is not null || string.IsNullOrEmpty(reference.Fragment) ||
                !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                Issue(item, "LIFECYCLE-URI", CheckStatus.Fail, "INVALID_MODIFIED_FILE", "Expected an internal XML path and exactly one leaf ID, without a query.");
                return;
            }
            var parts = path.Split('/', 2);
            try { item.Target = new(inputs.Baseline.ApplicationId, parts[0], parts.Length == 2 ? parts[1] : "", reference.Fragment); }
            catch (Exception exception) when (exception is ArgumentException or System.Xml.XmlException)
            {
                Issue(item, "LIFECYCLE-URI", CheckStatus.Fail, "TARGET_OUTSIDE_APPLICATION", "The target is not a leaf address in this application's sequence namespace.");
                return;
            }
            var sourceSequence = item.Document.LogicalPath.Split('/')[0];
            if (string.CompareOrdinal(parts[0], sourceSequence) > 0)
            {
                Issue(item, "LIFECYCLE-URI", CheckStatus.Fail, "TARGET_IN_FUTURE", "A lifecycle target cannot belong to a later sequence.");
                return;
            }
            if (!_files.TryGetValue(parts[0], out var files))
            {
                Issue(item, "HISTORY-BASELINE", CheckStatus.NotEvaluated, "HISTORY_NOT_AVAILABLE", "The referenced sequence is absent from the explicit baseline.");
                return;
            }
            if (!files.Contains(path))
            {
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.Fail, "TARGET_XML_NOT_FOUND", "The exact target XML does not exist in the selected source.");
                return;
            }
            if (!_documents.TryGetValue(path, out var document) || !document.ReadComplete)
            {
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.NotEvaluated, "TARGET_XML_UNINSPECTED", "The exact target XML could not be inspected under the selected profile.");
                return;
            }
            // Retain the specific ID defect even when DTD validation also found it.
            if (!document.IdIndex.TryGetValue(reference.Fragment, out var ids))
            {
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.Fail, "TARGET_LEAF_NOT_FOUND", "The target XML has no matching ID.");
                return;
            }
            if (ids.Count != 1)
            {
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.Fail, "TARGET_ID_AMBIGUOUS", "The target ID occurs more than once in this XML.");
                return;
            }
            if (!ids[0].IsLeaf)
            {
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.Fail, "TARGET_IS_NOT_LEAF", "The referenced ID belongs to a non-leaf XML element.");
                return;
            }
            if (document.DtdStatus != CheckStatus.Pass)
            {
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.NotEvaluated, "TARGET_XML_INVALID", "DTD-invalid XML cannot establish a valid lifecycle target.");
                return;
            }
            var target = document.Leaves.Single(leaf => leaf.NodePath == ids[0].NodePath);
            item.TargetLeaf = target;
            if (item.Document.ProfileSnapshotId != document.ProfileSnapshotId)
                Issue(item, "LIFECYCLE-CONTEXT", CheckStatus.NotEvaluated, "TARGET_PROFILE_INCOMPATIBLE", "Cross-profile lifecycle context is not qualified.");
            if (target.IsRegionalReference || item.Leaf.IsRegionalReference)
                Issue(item, "LIFECYCLE-CONTEXT", CheckStatus.Fail, "REGIONAL_REFERENCE_TARGET", "Regional backbone reference leaves do not participate in document lifecycle changes.");
            else if (!item.Leaf.ContextComplete || !target.ContextComplete)
                Issue(item, "LIFECYCLE-CONTEXT", CheckStatus.NotEvaluated, "TARGET_CONTEXT_UNRESOLVED", "Source and target require complete, unambiguous business contexts.");
            else if (item.Document.Kind != document.Kind || item.Leaf.ContextKey != target.ContextKey)
                Issue(item, "LIFECYCLE-CONTEXT", CheckStatus.Fail, "TARGET_CONTEXT_MISMATCH", "Source and target belong to different business contexts.");
            ResolveTargetContent(item, document, target);
        }

        private void ResolveTargetContent(Event item, ParsedBackbone document, ParsedPackageLeaf target)
        {
            if (target.Operation == "delete") return; // Inactive delete events are diagnosed during replay.
            PackageReference? content = null;
            try { if (!string.IsNullOrEmpty(target.Href)) content = PackageLogicalPath.ResolveReference(document.LogicalPath, target.Href); }
            catch (ArgumentException) { }
            if (content?.LogicalPath is not { } path || content.Query is not null)
            {
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.Fail, "TARGET_CONTENT_REFERENCE_INVALID", "The target leaf requires an internal content reference.");
                return;
            }
            var sequence = path.Split('/')[0];
            if (sequence.Length != 4 || sequence.Any(character => character is < '0' or > '9') ||
                string.CompareOrdinal(sequence, item.Target!.SequenceNumber) > 0)
            {
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.Fail, "TARGET_CONTENT_OUT_OF_SCOPE", "Historical content cannot escape the application or reference a future sequence.");
                return;
            }
            if (!_files.TryGetValue(sequence, out var files))
                Issue(item, "HISTORY-BASELINE", CheckStatus.NotEvaluated, "HISTORY_CONTENT_NOT_AVAILABLE", "The historical content source is not in the selected baseline.");
            else if (!files.Contains(path))
                Issue(item, "LIFECYCLE-TARGET", CheckStatus.Fail, "TARGET_CONTENT_NOT_FOUND", "The historical leaf's exact content file is missing.");
            else item.ContentPath = path;
        }

        private void CheckEffectiveness(Event item)
        {
            var target = item.Target!;
            var sequence = item.Document.LogicalPath.Split('/')[0];
            if (target.SequenceNumber == sequence)
            {
                Issue(item, "LIFECYCLE-EFFECTIVE", CheckStatus.NotEvaluated,
                    item.Leaf.Operation == "append" ? "SAME_SEQUENCE_APPEND_REQUIRES_POLICY" : "SAME_SEQUENCE_OPERATION_UNQUALIFIED",
                    "Same-sequence combinations require an explicit policy; ICH permits appropriate append cases after authority consultation.");
                return;
            }
            if (_states.TryGetValue(target, out var state) && state.Effectiveness is PackageLeafEffectiveness.Replaced or PackageLeafEffectiveness.Deleted or PackageLeafEffectiveness.DeleteEvent)
                Issue(item, "LIFECYCLE-EFFECTIVE", CheckStatus.Fail, "TARGET_NOT_EFFECTIVE", "The exact referenced leaf has already been replaced or deleted.");
            else if (state is null || state.Effectiveness == PackageLeafEffectiveness.Unknown ||
                     _uncertainSequences.Any(number => string.CompareOrdinal(number, target.SequenceNumber) >= 0 && string.CompareOrdinal(number, sequence) < 0))
                Issue(item, "LIFECYCLE-EFFECTIVE", CheckStatus.NotEvaluated, "TARGET_EFFECTIVENESS_UNKNOWN", "Unresolved historical events prevent establishing target effectiveness.");
            else if (_appendMembers.Contains(target))
                Issue(item, "LIFECYCLE-EFFECTIVE", CheckStatus.NotEvaluated, "APPEND_CHAIN_UNQUALIFIED",
                    "Further operations on an append branch require the cumulative lifecycle policy; no cascading invalidation is inferred.");
        }

        private void Apply(Event item)
        {
            if (item.Leaf.Address is not { } address) return;
            // A failed same-sequence append can make a new leaf's final effectiveness uncertain.
            var touchedByUnresolved = _unresolvedTargets.Contains(address);
            _states[address] = new(address, touchedByUnresolved ? PackageLeafEffectiveness.Unknown : item.Leaf.Operation == "delete"
                ? PackageLeafEffectiveness.DeleteEvent : PackageLeafEffectiveness.Current, item.Leaf.IsRegionalReference);
            if (item.Target is not { } target || item.Leaf.Operation == "new") return;
            if (item.Leaf.Operation is "replace" or "delete")
                _states[target] = _states[target] with { Effectiveness = item.Leaf.Operation == "replace"
                    ? PackageLeafEffectiveness.Replaced : PackageLeafEffectiveness.Deleted };
            else if (item.Leaf.Operation == "append")
            {
                _appendMembers.Add(target);
                _appendMembers.Add(address);
            }
        }

        private void Issue(Event item, string rule, CheckStatus status, string code, string message)
        {
            var field = code switch
            {
                "DELETE_CHECKSUM_NOT_EMPTY" => "@checksum",
                "DELETE_HAS_CONTENT" or "CONTENT_REFERENCE_REQUIRED" => "@xlink:href",
                "INVALID_OPERATION" or "REGIONAL_REFERENCE_MUST_BE_NEW" => "@operation",
                _ => "@modified-file"
            };
            var actual = field switch
            {
                "@checksum" => item.Leaf.Checksum,
                "@xlink:href" => item.Leaf.Href,
                "@operation" => item.Leaf.Operation,
                _ => item.Leaf.ModifiedFile
            };
            Add(rule, status, code, message, item.Leaf.Location with { FieldPath = field }, item, actual);
        }

        public PackageLifecycleInspection Finish()
        {
            var results = RuleIds.Select(rule =>
            {
                var findings = _findings.Where(finding => finding.RuleId == rule).ToArray();
                var status = findings.Any(finding => finding.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail :
                    Incomplete || findings.Any(finding => finding.CheckStatus == CheckStatus.NotEvaluated) ? CheckStatus.NotEvaluated : CheckStatus.Pass;
                return new RuleCheckResult(rule, status, status == CheckStatus.Pass ? "The check completed against the explicit input set."
                    : "See located observations; unresolved history and unsupported combinations remain incomplete.", findings);
            });
            return new(inputs, profile, Xml, _events.Select(item => new PackageLifecycleEvent(item.Leaf, item.Target,
                item.ContentPath, item.Status, Array.AsReadOnly(item.Findings.ToArray()))), _states.Values, results);
        }

        public sealed class Event(ParsedBackbone document, ParsedPackageLeaf leaf)
        {
            public ParsedBackbone Document { get; } = document;
            public ParsedPackageLeaf Leaf { get; } = leaf;
            public LeafAddress? Target { get; set; }
            public ParsedPackageLeaf? TargetLeaf { get; set; }
            public string? ContentPath { get; set; }
            public List<ValidationFinding> Findings { get; } = [];
            public CheckStatus Status => Findings.Any(finding => finding.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail :
                Findings.Count > 0 ? CheckStatus.NotEvaluated : CheckStatus.Pass;
        }
    }
}
