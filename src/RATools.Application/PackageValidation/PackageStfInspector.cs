using System.Xml;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class PackageStfInspector
{
    // This profile is used only by the low-level XML reader, never as an ICH lifecycle backbone.
    private static readonly PackageXmlBackboneProfile Profile = new(BackboneKind.Ich, "ectd:study", "study",
        "http://www.ich.org/ectd", "2.2", "stf-dtd", ["stf-dtd"], "", "STF-DTD", ["stf-style"]);

    internal static async Task<PackageStfInspection> InspectAsync(ICapturedPackageInput input, PackageXmlAssets assets,
        IReadOnlyList<ParsedBackbone> backbones, XmlInspectionFindings findings, CancellationToken cancellationToken)
    {
        var start = findings.Items.Count;
        var documents = new List<ParsedStudyTaggingFile>();
        var complete = backbones.Count > 0 && backbones.All(document => document.ReadComplete);
        try
        {
            if (!assets.Assets.TryGetValue("stf-dtd", out var dtd) || dtd.Sha256 != "e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f" ||
                dtd.LogicalPath != "util/dtd/ich-stf-v2-2.dtd")
            {
                Issue("STF_ASSET_UNAVAILABLE", "The selected snapshot has no qualified STF DTD asset.", null);
                return Finish();
            }
            var known = backbones.Select(document => document.LogicalPath).ToHashSet(StringComparer.Ordinal);
            var prefix = input.Manifest.SequenceNumber + "/";
            var files = input.Manifest.Files.ToDictionary(file => file.LogicalPath, StringComparer.Ordinal);
            var candidates = files.Keys.Where(path => !known.Contains(path) && !path.StartsWith(prefix + "util/", StringComparison.Ordinal) &&
                path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.Ordinal);
            foreach (var backbone in backbones)
                foreach (var leaf in backbone.Leaves.Where(leaf => leaf.Operation != "delete" && leaf.Href is not null))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var reference = PackageLogicalPath.ResolveReference(backbone.LogicalPath, leaf.Href!);
                        if (reference.LogicalPath is { } path && path.StartsWith(prefix, StringComparison.Ordinal) &&
                            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !known.Contains(path))
                        {
                            if (reference.Query is not null || reference.Fragment is not null)
                                Issue("STF_CANDIDATE_REFERENCE_UNRESOLVED", "An XML reference has a query or fragment; its STF role is not established.", path);
                            candidates.Add(path);
                        }
                    }
                    catch (ArgumentException) { Issue("STF_CANDIDATE_REFERENCE_UNRESOLVED", "A content reference cannot be resolved for STF discovery.", backbone.LogicalPath); }
                }
            foreach (var path in candidates.Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!files.TryGetValue(path, out var file))
                { Issue("STF_CANDIDATE_MISSING", "A referenced XML candidate is absent from the target sequence.", path); continue; }
                var named = path.Split('/')[^1].StartsWith("stf-", StringComparison.OrdinalIgnoreCase);
                var role = await ProbeAsync(input, path, findings, cancellationToken).ConfigureAwait(false);
                if (role is null) { complete = false; continue; }
                if (!role.Value)
                {
                    if (named) Issue("STF_CANDIDATE_ROOT_MISMATCH", "An STF-named candidate does not have a study root or declaration.", path);
                    continue;
                }
                if (backbones.Count + documents.Count >= input.Limits.MaxBackbones)
                { Issue("STF_DOCUMENT_LIMIT", "STF and backbone count exceeds the selected document budget.", path); break; }
                var before = findings.Items.Count;
                var read = await PackageXmlDocumentReader.ReadAsync(input, path, assets, Profile, findings, cancellationToken).ConfigureAwait(false);
                var observations = findings.Items.Skip(before).ToArray();
                var status = observations.Any(item => item.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail :
                    !read.Complete || observations.Any(item => item.CheckStatus == CheckStatus.NotEvaluated) ? CheckStatus.NotEvaluated : CheckStatus.Pass;
                complete &= read.Complete && status == CheckStatus.Pass;
                documents.Add(new(path, file.Sha256, read.Complete, status, read.SystemId, read.Elements, read.ProcessingInstructions, read.ResolvedAssets));
                if (findings.StructureLimitReached || findings.Truncated) { complete = false; break; }
            }
        }
        catch (XmlInspectionLimitException) { complete = false; }
        cancellationToken.ThrowIfCancellationRequested();
        return Finish();

        void Issue(string code, string message, string? path)
        {
            complete = false;
            findings.Add(new("STF-DTD", CheckStatus.NotEvaluated, ValidationSeverity.Warning, code, message,
                new(input.Manifest.SequenceNumber, path)));
        }
        PackageStfInspection Finish() => new(complete && !findings.Truncated && !findings.StructureLimitReached,
            documents, findings.Items.Skip(start));
    }

    private static async Task<bool?> ProbeAsync(ICapturedPackageInput input, string path, XmlInspectionFindings findings, CancellationToken token)
    {
        try
        {
            using var reader = XmlReader.Create(new PackageXmlDocumentReader.CancellationInputStream(input.OpenRead(path), token), new XmlReaderSettings
            {
                Async = true, DtdProcessing = DtdProcessing.Parse, XmlResolver = null, CloseInput = true,
                MaxCharactersInDocument = input.Limits.MaxXmlCharacters, MaxCharactersFromEntities = input.Limits.MaxXmlEntityCharacters
            });
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                findings.CountNode(reader.Depth + 1, new(input.Manifest.SequenceNumber, path));
                if (reader.NodeType == XmlNodeType.DocumentType)
                {
                    if (reader.Value.Length != 0) throw new XmlException("Internal DTD declarations are not allowed during STF discovery.");
                    // Observe the declaration before a root whose namespace may be supplied
                    // by the fixed DTD. The null resolver cannot fetch its external subset.
                    if (reader.Name == "ectd:study") return true;
                }
                if (reader.NodeType == XmlNodeType.Element)
                    return reader.LocalName == "study" && reader.NamespaceURI == "http://www.ich.org/ectd";
            }
        }
        catch (Exception exception) when (exception is XmlException or IOException)
        {
            findings.Add(new("STF-DTD", CheckStatus.NotEvaluated, ValidationSeverity.Warning, "STF_DISCOVERY_UNREADABLE",
                "An XML candidate could not supply safe root evidence.", new(input.Manifest.SequenceNumber, path)));
            return null;
        }
        findings.Add(new("STF-DTD", CheckStatus.NotEvaluated, ValidationSeverity.Warning, "STF_DISCOVERY_EMPTY",
            "An XML candidate did not provide a root element.", new(input.Manifest.SequenceNumber, path)));
        return null;
    }
}
