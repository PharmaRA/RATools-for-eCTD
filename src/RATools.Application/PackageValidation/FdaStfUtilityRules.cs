using System.Xml;
using System.Xml.Linq;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class FdaStfUtilityRules
{
    private const string DtdMd5 = "0972c10a4dadf3df5d2f41b2026a4a5c";
    private const string StyleMd5 = "92bfeb4cb358a4af786317a0da2309c5";
    private const string ValuesMd5 = "797b4110bddb6d3cb4ad3869ffccb53c";

    internal static RuleCheckResult Inspect(ValidationRuleDescriptor rule, ICapturedPackageInput target,
        PackageLifecycleInspection lifecycle, PackageStandardsContext context, int remaining, CancellationToken token)
    {
        var id = rule.InternalRuleId;
        var checksum = id == FdaRequiredUtilityRules.ChecksumRuleId;
        token.ThrowIfCancellationRequested();
        var xml = lifecycle.XmlInspections.Where(item => item.InputDigest == target.InputDigest && item.ProfileSnapshotId == lifecycle.ProfileSnapshotId).Take(2).ToArray();
        if (lifecycle.InputDigest != target.InputDigest || lifecycle.RulesDigest != context.Catalog.Digest ||
            lifecycle.AssetManifestDigest != context.Assets.Digest || lifecycle.LimitsDigest != target.Limits.Digest() ||
            xml.Length != 1 || xml[0].RulesDigest != context.Catalog.Digest || xml[0].AssetManifestDigest != context.Assets.Digest ||
            xml[0].LimitsDigest != target.Limits.Digest()) return Unknown("Target XML facts must be unique and bound to the capture, rules, assets and limits.");
        var stfs = xml[0].StudyTaggingFiles;
        if (stfs.Documents.Count == 0) return stfs.InventoryComplete ? new(id, CheckStatus.Pass, "No target STF candidates require conditional utilities.") :
            Unknown("STF discovery is incomplete; conditional utilities remain unresolved.");
        if (rule.AuthorityRuleId != (checksum ? "1130" : "1119") || rule.RuleVersion != "4.7.1" || rule.SourceId != "fda-validation-criteria-4.7" ||
            !context.Catalog.Sources.TryGetValue(rule.SourceId, out var source) || source.Kind != RuleSourceKind.RegulatorySpecification ||
            source.Version != "4.7" || source.VerificationStatus != "Verified" || source.Sha256 != "2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c" ||
            !context.HasArchivedOfficialSource(source) || context.SelectionDate is null ||
            !context.HasArchivedOfficialSource(BundledOfficialSources.IchQa) || !context.HasArchivedOfficialSource(BundledOfficialSources.StfSpecification))
            return Unknown("STF utility assessment requires the archived criteria, STF specification, Q&A and selection date.");
        var incomplete = !stfs.InventoryComplete;
        var findings = new List<ValidationFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var work = target.Limits.MaxXmlNodes;
        var files = new Dictionary<string, PackageInputFile>(StringComparer.Ordinal);
        foreach (var file in target.Manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || !files.TryAdd(file.LogicalPath, file)) return Unknown("Captured utility inventory is ambiguous or exceeds the work limit.");
        }
        var documentPaths = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in stfs.Documents)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0) return Unknown("STF identity inventory exceeds the work limit.");
            if (!documentPaths.Add(document.LogicalPath)) duplicates.Add(document.LogicalPath);
        }
        var prefix = target.Manifest.SequenceNumber + "/";
        foreach (var document in stfs.Documents)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0) { incomplete = true; break; }
            if (!document.LogicalPath.StartsWith(prefix, StringComparison.Ordinal) || duplicates.Contains(document.LogicalPath) ||
                !files.TryGetValue(document.LogicalPath, out var captured) || captured.Sha256 != document.CapturedSha256 ||
                !HasStudyRoot(document, ref work, token)) { incomplete = true; continue; }
            // A declaration-only discovery candidate is not an established STF. Conversely,
            // a later DTD/content error cannot erase an already observed actual study root.
            if (!document.ReadComplete) incomplete = true;
            var dtdPath = Resolve(document.LogicalPath, document.SystemIdentifier, prefix + "util/dtd/");
            if (dtdPath != prefix + "util/dtd/ich-stf-v2-2.dtd") incomplete = true;
            else Check(dtdPath, DtdMd5, BundledOfficialSources.StfDtdChecksum);
            var instructions = document.ProcessingInstructions.Where(item => item.Target == "xml-stylesheet").ToArray();
            if (instructions.Length == 0) incomplete = true; // Actual creation-time stylesheet cannot be guessed from filenames.
            foreach (var instruction in instructions)
            {
                token.ThrowIfCancellationRequested();
                if (work-- <= 0) { incomplete = true; break; }
                var href = StylesheetHref(instruction.Data, target.Limits.MaxXmlCharacters);
                var path = Resolve(document.LogicalPath, href, prefix + "util/style/");
                if (path is null) { incomplete = true; continue; }
                files.TryGetValue(path, out var declaredStyle);
                var knownStyle = path.EndsWith("/ich-stf-stylesheet-2-3.xsl", StringComparison.Ordinal) ||
                    string.Equals(declaredStyle?.Md5, StyleMd5, StringComparison.OrdinalIgnoreCase);
                if (checksum && (!knownStyle || context.SelectionDate < new DateOnly(2017, 6, 1))) { incomplete = true; continue; }
                var style = Check(path, StyleMd5, BundledOfficialSources.StfStyleChecksum);
                if (!string.Equals(style?.Md5, StyleMd5, StringComparison.OrdinalIgnoreCase)) { incomplete = true; continue; }
                // Only the exact supported stylesheet proves its document('valid-values.xml') dependency.
                if (!context.HasArchivedOfficialSource(BundledOfficialSources.StfPage) ||
                    !context.HasArchivedOfficialSource(BundledOfficialSources.StfStyleChecksum))
                { incomplete = true; continue; }
                var valuesPath = path[..(path.LastIndexOf('/') + 1)] + "valid-values.xml";
                if (!checksum) Check(valuesPath, ValuesMd5, BundledOfficialSources.StfPage);
                else if (context.SelectionDate >= new DateOnly(2025, 2, 18) && context.HasArchivedOfficialSource(BundledOfficialSources.StfValues) &&
                    context.HasArchivedOfficialSource(BundledOfficialSources.StfPage))
                {
                    // Other valid-values versions are not automatically corrupt version 6.0.
                    if (!files.TryGetValue(valuesPath, out var values) || values.Md5 != ValuesMd5) incomplete = true;
                }
                else incomplete = true;
            }
        }
        return new(id, findings.Count > 0 ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Assessed capture-bound actual STF roots with conditional STF 2.2 DTD, declared stylesheet and recognized valid-values 6.0 evidence. Declaration-only, conflicting or unbound candidates remain unknown. Creation-time ICH styles and other supported versions remain unqualified.", findings);

        PackageInputFile? Check(string path, string md5, ValidationRuleSource evidence)
        {
            token.ThrowIfCancellationRequested();
            files.TryGetValue(path, out var file);
            if (checksum && !context.HasArchivedOfficialSource(evidence)) { incomplete = true; return file; }
            if (file is null && checksum) { incomplete = true; return null; }
            if ((!checksum && file is not null) || checksum && string.Equals(file!.Md5, md5, StringComparison.OrdinalIgnoreCase)) return file;
            if (!seen.Add(path)) return file;
            if (remaining <= 0) { incomplete = true; return file; }
            remaining--;
            findings.Add(new(id, CheckStatus.Fail, rule.Severity, id,
                checksum ? "The delivered conditional STF utility differs from the published checksum." : "The required conditional STF utility is absent at its referenced delivery path.",
                new(target.Manifest.SequenceNumber, path, Files: file is null ? [] : [new(target.SourceId, path)]),
                Expected: checksum ? md5 : path[prefix.Length..], Actual: checksum ? file!.Md5 : null));
            return file;
        }
        RuleCheckResult Unknown(string reason) => new(id, CheckStatus.NotEvaluated, reason);
    }

    private static bool HasStudyRoot(ParsedStudyTaggingFile document, ref int work, CancellationToken token)
    {
        ParsedXmlElement? root = null;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in document.Elements)
        {
            token.ThrowIfCancellationRequested();
            if (work-- <= 0 || !paths.Add(element.NodePath)) return false;
            if (element.ParentPath is not null) continue;
            if (root is not null) return false;
            root = element;
        }
        return root is { LocalName: "study", NamespaceUri: "http://www.ich.org/ectd" } &&
            root.Location.LogicalPath == document.LogicalPath && root.Location.NodePath == root.NodePath;
    }

    private static string? Resolve(string source, string? href, string prefix)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(href)) return null;
            var reference = PackageLogicalPath.ResolveReference(source, href);
            return reference.Query is null && reference.Fragment is null && reference.LogicalPath?.StartsWith(prefix, StringComparison.Ordinal) == true ? reference.LogicalPath : null;
        }
        catch (ArgumentException) { return null; }
    }

    private static string? StylesheetHref(string data, long maximum)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader("<style " + data + "/>"),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = maximum });
            var element = XElement.Load(reader);
            return element.Name == "style" && !element.Elements().Any() && element.Attribute("type")?.Value == "text/xsl" ? element.Attribute("href")?.Value : null;
        }
        catch (XmlException) { return null; }
    }
}
