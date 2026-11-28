using System.Xml;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaRegionalResourceLocationRule
{
    internal const string RuleId = "FDA47-2003";
    private static readonly ValidationRuleSource M1Specification = new("fda-m1-specification", RuleSourceKind.RegulatorySpecification, "2.6",
        "https://www.fda.gov/media/159382/download?attachment", "1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2", "Verified");
    internal static RuleCheckResult? Inspect(PackageInputSet packages, PackageLifecycleInspection lifecycle,
        PackageStandardsContext context, int remaining, CancellationToken cancellationToken, Action<RuleCheckResult>? retainProgress = null)
    {
        var rule = context.Catalog.ForProfile(lifecycle.ProfileSnapshotId).SingleOrDefault(item => item.InternalRuleId == RuleId);
        if (rule is null) return null;
        retainProgress?.Invoke(Unknown("The regional resource location inspection did not complete."));
        cancellationToken.ThrowIfCancellationRequested();
        if (rule.AuthorityRuleId != "2003" || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null)
            return Unknown("Captured FDA criteria and an explicit selection date are required.");
        if (context.SelectionDate < new DateOnly(2015, 6, 15))
            return new(RuleId, CheckStatus.NotApplicable, "The selection date precedes FDA 2003.");
        if (context.SelectionDate < new DateOnly(2025, 3, 31) || !context.HasArchivedOfficialSource(M1Specification))
            return Unknown("Only the captured M1 specification v2.6 applicability period is qualified for this component.");
        var target = packages.Target;
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.HistoryManifestDigest != packages.Baseline.Digest ||
            lifecycle.RulesDigest != context.Catalog.Digest || lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest())
            return Unknown("The lifecycle facts are not bound to the selected input and context.");
        var work = target.Limits.MaxXmlNodes;
        var findings = new List<ValidationFinding>();
        try
        {
            var matches = new List<PackageXmlInspection>();
            foreach (var candidate in lifecycle.XmlInspections) { Tick(); if (candidate.InputDigest == target.InputDigest) matches.Add(candidate); }
            if (matches.Count != 1) return Unknown("A unique current XML inspection is required.");
            var xml = matches[0];
            if (xml.ProfileSnapshotId != lifecycle.ProfileSnapshotId || xml.RulesDigest != context.Catalog.Digest ||
                xml.AssetManifestDigest != context.Assets.Digest || xml.LimitsDigest != target.Limits.Digest()) return Unknown("XML observation binding is inconsistent.");
            foreach (var document in xml.Backbones)
            {
                Tick();
                if (document.Kind != BackboneKind.UsRegional || document.ProfileSnapshotId != xml.ProfileSnapshotId ||
                    document.DocumentTypeName != "fda-regional:fda-regional" || !document.LogicalPath.StartsWith(target.Manifest.SequenceNumber + "/", StringComparison.Ordinal)) continue;
                var files = 0; var observations = 0;
                foreach (var file in target.Manifest.Files) { Tick(); if (file.LogicalPath == document.LogicalPath) files++; }
                foreach (var other in xml.Backbones) { Tick(); if (other.LogicalPath == document.LogicalPath) observations++; }
                foreach (var other in xml.StudyTaggingFiles.Documents) { Tick(); if (other.LogicalPath == document.LogicalPath) observations++; }
                if (files != 1 || observations != 1) continue;
                var roots = new List<ParsedXmlElement>();
                foreach (var element in document.Elements) { Tick(); if (element.ParentPath is null) roots.Add(element); }
                if (roots.Count != 1 || roots[0] is not { Name: "fda-regional:fda-regional", LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" } ||
                    roots[0].Attribute("dtd-version") != "3.3" || roots[0].Location.LogicalPath != document.LogicalPath || roots[0].Location.SequenceNumber != target.Manifest.SequenceNumber) continue;
                if (ProhibitedLocal(document.LogicalPath, document.SystemIdentifier))
                    Fail(document.SystemIdentifier!, roots[0].Location with { FieldPath = "doctype/system" });
                // A fully observed PI may establish a local violation even when later XML
                // fails. Partial observations never establish absence or a whole-rule Pass.
                var instructions = document.ReadComplete ? document.ProcessingInstructions : document.PartialProcessingInstructions;
                foreach (var instruction in instructions)
                {
                    Tick();
                    if (instruction.Target != "xml-stylesheet" || instruction.Location.LogicalPath != document.LogicalPath || instruction.Location.SequenceNumber != target.Manifest.SequenceNumber) continue;
                    var href = StyleHref(instruction.Data);
                    if (ProhibitedLocal(document.LogicalPath, href)) Fail(href!, instruction.Location with { FieldPath = "xml-stylesheet/@href" });
                }
            }
        }
        catch (LocationWorkLimitException) { }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RuleId, findings.Count > 0 ? CheckStatus.Fail : CheckStatus.NotEvaluated,
            "Detected source-prohibited local relative and qualified local file-URI DTD/stylesheet locations, including fully observed PI events before later XML damage after regional root binding. Partial PI inventory cannot establish absence. Historical applicability, other URI forms, missing or unavailable declarations, approved-URL parsing and stylesheet version identity remain unqualified.", findings);

        void Tick() { cancellationToken.ThrowIfCancellationRequested(); if (work-- <= 0) throw new LocationWorkLimitException(); }
        bool ProhibitedLocal(string owner, string? value)
        {
            Tick();
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Contains('\\') || value.Contains('?') || value.Contains('#')) return false;
            if (value.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                return Uri.TryCreate(value, UriKind.Absolute, out var fileUri) && fileUri.IsFile && fileUri.IsWellFormedOriginalString() &&
                    fileUri.Host.Length == 0 && fileUri.Query.Length == 0 && fileUri.Fragment.Length == 0 && fileUri.AbsolutePath.Length > 0;
            if (value.StartsWith('/') || value.Contains(':') || Uri.TryCreate(value, UriKind.Absolute, out _)) return false;
            try { return PackageLogicalPath.ResolveReference(owner, value).LogicalPath is not null; }
            catch (ArgumentException) { return false; }
        }
        string? StyleHref(string data)
        {
            Tick();
            if (data.Length > target.Limits.MaxXmlCharacters) return null;
            try
            {
                using var reader = XmlReader.Create(new StringReader("<style " + data + "/>"), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = target.Limits.MaxXmlCharacters });
                if (!reader.Read() || reader.NodeType != XmlNodeType.Element || reader.Name != "style") return null;
                var href = reader.GetAttribute("href");
                if (!reader.IsEmptyElement || reader.Read()) return null;
                return href;
            }
            catch (XmlException) { return null; }
        }
        void Fail(string actual, ValidationLocation location)
        {
            Tick();
            if (remaining <= 0) return;
            remaining--;
            findings.Add(new(RuleId, CheckStatus.Fail, rule.Severity, RuleId,
                "The captured M1 specification requires website addresses instead of local paths for regional supporting resources.",
                location with { Files = [new(target.SourceId, location.LogicalPath!)] }, "A qualified website resource location", actual));
            retainProgress?.Invoke(new(RuleId, CheckStatus.Fail, "A prohibited local resource location is established; other evidence may remain unavailable.", findings.ToArray()));
        }
    }
    private static RuleCheckResult Unknown(string message) => new(RuleId, CheckStatus.NotEvaluated, message);
    private sealed class LocationWorkLimitException : Exception;
}
