using System.Xml;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class PackageStfInspector
{
    // This profile is used only by the low-level XML reader, never as an ICH lifecycle backbone.
    private static readonly PackageXmlBackboneProfile Profile = new(BackboneKind.Ich, "ectd:study", "study",
        "http://www.ich.org/ectd", "2.2", "stf-dtd", ["stf-dtd"], "", "STF-DTD", ["stf-style"]);

    internal static async Task<PackageStfInspection> InspectAsync(ICapturedPackageInput input, PackageXmlAssets assets,
        IReadOnlyList<ParsedBackbone> backbones, XmlInspectionFindings findings, CancellationToken cancellationToken,
        Action<PackageStfInspection>? retainProgress = null)
    {
        var start = findings.Items.Count;
        var documents = new List<ParsedStudyTaggingFile>();
        var regionalCandidates = new List<ParsedStudyTaggingFile>();
        var complete = backbones.Count > 0 && backbones.All(document => document.ReadComplete);
        var prefixWork = input.Limits.MaxXmlNodes;
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
            var candidates = files.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal) &&
                !known.Contains(path) && !path.StartsWith(prefix + "util/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
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
                var xmlExtension = path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
                if (!xmlExtension)
                {
                    var prefixRole = await XmlPrefixAsync(input, path, SpendPrefixWork, cancellationToken).ConfigureAwait(false);
                    if (prefixRole == false) continue;
                    if (prefixRole is null)
                    { Issue("STF_PREFIX_UNRESOLVED", "A bounded content prefix could not establish or exclude XML content.", path); continue; }
                }
                var named = path.Split('/')[^1].StartsWith("stf-", StringComparison.OrdinalIgnoreCase);
                var role = await ProbeAsync(input, path, findings, cancellationToken).ConfigureAwait(false);
                if (role is null) { complete = false; continue; }
                if (!role.Value)
                {
                    if (named && xmlExtension) Issue("STF_CANDIDATE_ROOT_MISMATCH", "An STF-named candidate does not have a study root or declaration.", path);
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
                Retain();
                if (findings.StructureLimitReached || findings.Truncated) { complete = false; break; }
            }
            // A module-one reference may have sent a real STF through the regional reader,
            // which stops before publishing its root. Recover a separate, pinned-profile
            // observation without erasing the regional failure or changing ordinary STF scope.
            var recoveryPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var backbone in backbones)
            {
                SpendPrefixWork();
                if (backbone.Kind != BackboneKind.Ich || backbone.LogicalPath != prefix + "index.xml" ||
                    !backbone.ReadComplete || backbone.DtdStatus != CheckStatus.Pass) continue;
                foreach (var leaf in backbone.Leaves)
                {
                    SpendPrefixWork();
                    if (!leaf.IsRegionalReference || leaf.Operation is not ("new" or "append" or "replace") || leaf.Href is null) continue;
                    PackageReference reference;
                    try { reference = PackageLogicalPath.ResolveReference(backbone.LogicalPath, leaf.Href); }
                    catch (ArgumentException) { continue; }
                    if (reference.LogicalPath is not { } path || !path.StartsWith(prefix, StringComparison.Ordinal) ||
                        reference.Query is not null || reference.Fragment is not null || !files.ContainsKey(path)) continue;
                    var matches = new List<ParsedBackbone>();
                    foreach (var observed in backbones) { SpendPrefixWork(); if (observed.LogicalPath == path) matches.Add(observed); }
                    if (matches.Count == 1 && matches[0].Kind == BackboneKind.UsRegional && !matches[0].ReadComplete &&
                        matches[0].DtdStatus != CheckStatus.Pass && matches[0].Elements.Count == 0) recoveryPaths.Add(path);
                }
            }
            foreach (var path in recoveryPaths.Order(StringComparer.Ordinal))
            {
                SpendPrefixWork();
                if (findings.StructureLimitReached || findings.Truncated) break;
                if (backbones.Count + documents.Count + regionalCandidates.Count >= input.Limits.MaxBackbones)
                { Issue("STF_DOCUMENT_LIMIT", "STF recovery exceeds the selected document budget.", path); break; }
                if (await ProbeAsync(input, path, findings, cancellationToken).ConfigureAwait(false) != true) continue;
                var before = findings.Items.Count;
                var read = await PackageXmlDocumentReader.ReadAsync(input, path, assets, Profile, findings, cancellationToken).ConfigureAwait(false);
                var observations = findings.Items.Skip(before).ToArray();
                var status = observations.Any(item => item.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail :
                    !read.Complete || observations.Any(item => item.CheckStatus == CheckStatus.NotEvaluated) ? CheckStatus.NotEvaluated : CheckStatus.Pass;
                regionalCandidates.Add(new(path, files[path].Sha256, read.Complete, status, read.SystemId,
                    read.Elements, read.ProcessingInstructions, read.ResolvedAssets));
                Retain();
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
            documents, findings.Items.Skip(start), regionalCandidates);
        void Retain() => retainProgress?.Invoke(new(false, documents, findings.Items.Skip(start), regionalCandidates));
        void SpendPrefixWork()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (prefixWork-- > 0) return;
            Issue("STF_PREFIX_WORK_LIMIT", "Content discovery exhausted its shared prefix work budget.", null);
            throw new XmlInspectionLimitException("STF prefix work limit exceeded.");
        }
    }

    // A prefix is only a candidate discriminator. The XML reader always reopens
    // the original bytes; this routine cannot repair malformed XML or assert an STF role.
    private static async Task<bool?> XmlPrefixAsync(ICapturedPackageInput input, string path, Action spend, CancellationToken token)
    {
        const int maxPrefix = 4096;
        var bytes = new byte[maxPrefix];
        var length = 0;
        try
        {
            spend();
            using var stream = input.OpenRead(path);
            while (length < maxPrefix)
            {
                spend();
                var read = await stream.ReadAsync(bytes.AsMemory(length, Math.Min(256, maxPrefix - length)), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                length += read;
                if (length < 4 && read != 0) continue; // Short reads are not EOF.
                var result = Classify(bytes, length, read == 0);
                if (result.HasValue || read == 0) return result;
            }
        }
        catch (IOException) { return null; }
        return null;

        static bool? Classify(byte[] bytes, int length, bool eof)
        {
            if (length == 0) return eof ? false : null;
            var width = 1;
            var bigEndian = false;
            var start = 0;
            if (length >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0) { width = 4; start = 4; }
            else if (length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff) { width = 4; bigEndian = true; start = 4; }
            else if (length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) { width = 2; start = 2; }
            else if (length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) { width = 2; bigEndian = true; start = 2; }
            else if (length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) start = 3;
            else if (length >= 4 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0) width = 4;
            else if (length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0) { width = 4; bigEndian = true; }
            else if (length >= 4 && bytes[1] == 0 && bytes[3] == 0) width = 2;
            else if (length >= 4 && bytes[0] == 0 && bytes[2] == 0) { width = 2; bigEndian = true; }
            // Incomplete/unsupported encoding signatures must not become negative XML evidence.
            else if (Partial([0xff, 0xfe, 0, 0]) || Partial([0, 0, 0xfe, 0xff]) || Partial([0xfe, 0xff]) || Partial([0xef, 0xbb, 0xbf]) ||
                length < 4 && (bytes[0] == 0 || length >= 2 && bytes[1] == 0 && bytes[0] is 0x09 or 0x0a or 0x0d or 0x20 or 0x3c) ||
                length >= 4 && bytes[0] == 0x4c && bytes[1] == 0x6f && bytes[2] == 0xa7 && bytes[3] == 0x94) return null;
            for (var offset = start; offset + width <= length; offset += width)
            {
                uint value = 0;
                for (var part = 0; part < width; part++)
                    value |= (uint)bytes[offset + part] << (8 * (bigEndian ? width - part - 1 : part));
                if (value is 0x20 or 0x09 or 0x0a or 0x0d) continue;
                return value == '<';
            }
            return null;

            bool Partial(byte[] signature) => length < signature.Length && bytes.AsSpan(0, length).SequenceEqual(signature.AsSpan(0, length));
        }
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
