using System.Text;
using System.Xml;
using System.Xml.Linq;
using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed class PackageFileInspector(TimeProvider? timeProvider = null)
{
    private static readonly string[] RuleIds = ["FILE-HREF", "FILE-MD5", "INDEX-MD5", "DELIVERY-ASSETS", "DELIVERY-STRUCTURE", "ZIP-PARITY", "FILE-NAMING"];

    public async Task<PackageFileInspection> InspectAsync(PackageDeliveryInput input, PackageLifecycleInspection lifecycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(lifecycle);
        if (lifecycle.InputDigest != input.Packages.Target.InputDigest || lifecycle.HistoryManifestDigest != input.Packages.Baseline.Digest ||
            lifecycle.LimitsDigest != input.Packages.Target.Limits.Digest() || lifecycle.RulesDigest != PackageValidationCatalog.Current.Digest ||
            lifecycle.AssetManifestDigest != PackageXmlAssets.Current.Digest)
            throw new ArgumentException("XML/lifecycle observations must bind this exact primary input, history, rules, assets and limits.", nameof(lifecycle));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(input.Packages.Target.Limits.MaxElapsedSeconds), timeProvider ?? TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var run = new Inspection(input, lifecycle, linked.Token);
        try
        {
            await input.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
            await run.CheckFilesAsync().ConfigureAwait(false);
            run.Compare();
            await input.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
        }
        catch (FileInspectionLimitException)
        {
            run.Incomplete = true;
            run.Terminal("FILE_FINDING_LIMIT", "File inspection stopped at the configured finding limit.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            run.Incomplete = true;
            run.Terminal("FILE_INSPECTION_TIMEOUT", "File inspection exceeded the configured deadline.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return run.Finish();
    }

    private sealed class FileInspectionLimitException : Exception;

    private sealed class Inspection(PackageDeliveryInput input, PackageLifecycleInspection lifecycle, CancellationToken cancellationToken)
    {
        private readonly List<ValidationFinding> _findings = [];
        private readonly List<PackageFileReference> _references = [];
        private readonly Dictionary<string, PackageInputFile> _files = input.Packages.Sequences.Values
            .SelectMany(capture => capture.Manifest.Files).ToDictionary(file => file.LogicalPath, StringComparer.Ordinal);
        public bool Incomplete { get; set; }

        private void Add(string rule, CheckStatus status, string code, string message, ValidationLocation location,
            string? expected = null, string? actual = null, ValidationSeverity? severity = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_findings.Count >= input.Packages.Target.Limits.MaxXmlFindings - 1) throw new FileInspectionLimitException();
            _findings.Add(new(rule, status, severity ?? (status == CheckStatus.Fail ? ValidationSeverity.Error : ValidationSeverity.Warning),
                code, message, location, expected, actual));
        }

        public void Terminal(string code, string message) => _findings.Add(new("FILE-HREF", CheckStatus.NotEvaluated,
            ValidationSeverity.Error, code, message, new(input.Packages.Target.Manifest.SequenceNumber)));

        public async Task CheckFilesAsync()
        {
            var documents = lifecycle.XmlInspections.SelectMany(xml => xml.Backbones).ToArray();
            if (lifecycle.XmlInspections.Count != input.Packages.Sequences.Count || documents.Any(document => !document.ReadComplete))
            {
                Incomplete = true;
                Add("FILE-HREF", CheckStatus.NotEvaluated, "XML_REFERENCES_INCOMPLETE", "Unreadable backbones prevent a complete file reference inventory.", new());
            }
            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CheckStylesheetsAsync(document).ConfigureAwait(false);
                foreach (var leaf in document.Leaves)
                {
                    var location = leaf.Location with { FieldPath = "@xlink:href" };
                    if (leaf.Operation == "delete") continue; // Operation/checksum semantics are checked even without href by P2-04.
                    if (string.IsNullOrWhiteSpace(leaf.Href))
                    {
                        Add("FILE-HREF", CheckStatus.Fail, "FILE_REFERENCE_REQUIRED", "A content leaf has no file reference.", location);
                        MissingChecksumEvidence(leaf.Location);
                        continue;
                    }
                    var reference = Resolve(document.LogicalPath, leaf.Href, location);
                    var exists = reference?.LogicalPath is { } path && _files.ContainsKey(path);
                    _references.Add(new(document.LogicalPath, leaf.Href, reference, location, exists));
                    if (!exists)
                    {
                        MissingChecksumEvidence(leaf.Location);
                        continue;
                    }
                    var file = _files[reference!.LogicalPath!];
                    // Capture computed hashes from actual bytes and is rehashed before/after the check.
                    if (leaf.ChecksumType != "md5" || !IsMd5(leaf.Checksum))
                        Add("FILE-MD5", CheckStatus.Fail, "INVALID_FILE_CHECKSUM", "Expected checksum-type md5 and a 32-digit hexadecimal checksum.",
                            leaf.Location with { FieldPath = "@checksum" }, "md5; 32 hexadecimal digits", leaf.ChecksumType + "; " + leaf.Checksum);
                    else if (!string.Equals(leaf.Checksum, file.Md5, StringComparison.OrdinalIgnoreCase))
                        Add("FILE-MD5", CheckStatus.Fail, "FILE_CHECKSUM_MISMATCH", "The declared MD5 differs from the selected physical file bytes.",
                            leaf.Location with { FieldPath = "@checksum" }, file.Md5, leaf.Checksum);
                }
            }
            foreach (var capture in input.Packages.Sequences.Values.OrderBy(capture => capture.Manifest.SequenceNumber, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CheckIndexAsync(capture).ConfigureAwait(false);
                var profile = input.Packages.Baseline.Entries.SingleOrDefault(entry => entry.SequenceNumber == capture.Manifest.SequenceNumber)?.ProfileSnapshotId
                    ?? lifecycle.ProfileSnapshotId;
                IReadOnlyList<PackageXmlBackboneProfile> profiles;
                try { profiles = PackageXmlAssets.Current.ForProfile(profile); }
                catch (ArgumentException)
                {
                    Add("DELIVERY-ASSETS", CheckStatus.NotEvaluated, "DELIVERY_PROFILE_UNSUPPORTED", "The selected historical asset profile is unsupported.", new(capture.Manifest.SequenceNumber));
                    continue;
                }
                foreach (var assetId in profiles.SelectMany(profile => profile.AllowedAssetIds).Distinct(StringComparer.Ordinal))
                {
                    var asset = PackageXmlAssets.Current.Assets[assetId];
                    var path = capture.Manifest.SequenceNumber + "/" + asset.LogicalPath;
                    if (!_files.TryGetValue(path, out var file))
                        Add("DELIVERY-ASSETS", CheckStatus.Fail, "DELIVERY_ASSET_MISSING", "A required pinned DTD or module is not delivered.", Locate(path), asset.Sha256);
                    else if (file.Sha256 != asset.Sha256)
                        Add("DELIVERY-ASSETS", CheckStatus.Fail, "DELIVERY_ASSET_CHANGED", "A delivered DTD or module differs from the pinned profile bytes.", Locate(path), asset.Sha256, file.Sha256);
                }
                // DTD verification is complete; style acceptance and profile exceptions require their own asset policy.
                Add("DELIVERY-ASSETS", CheckStatus.NotEvaluated, "STYLESHEET_POLICY_UNQUALIFIED",
                    "Declared stylesheet references and pinned bytes were checked; creation-time stylesheet evidence and complete regional style policy remain unqualified.", new(capture.Manifest.SequenceNumber));
                Inventory(capture);
            }
        }

        private void MissingChecksumEvidence(ValidationLocation location) => Add("FILE-MD5", CheckStatus.NotEvaluated,
            "FILE_CHECKSUM_UNRESOLVED", "The exact referenced file is unavailable; its checksum cannot be compared.", location);

        private async Task CheckStylesheetsAsync(ParsedBackbone document)
        {
            var queue = new Queue<(string Source, string Href, ValidationLocation Location)>();
            foreach (var instruction in document.ProcessingInstructions.Where(instruction => instruction.Target == "xml-stylesheet"))
            {
                string? href = null;
                try
                {
                    using var reader = XmlReader.Create(new StringReader("<style " + instruction.Data + "/>"),
                        new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = input.Packages.Target.Limits.MaxXmlCharacters });
                    var element = XElement.Load(reader);
                    if (element.Name == "style" && !element.Elements().Any()) href = element.Attribute("href")?.Value;
                }
                catch (XmlException) { }
                if (string.IsNullOrWhiteSpace(href))
                    Add("DELIVERY-ASSETS", CheckStatus.Fail, "INVALID_STYLESHEET_INSTRUCTION", "The stylesheet processing instruction requires valid pseudo-attributes and an href.", instruction.Location);
                else queue.Enqueue((document.LogicalPath, href, instruction.Location));
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var styles = PackageXmlAssets.Current.ForProfile(document.ProfileSnapshotId).SelectMany(profile => profile.StylesheetAssetIds)
                .Select(id => PackageXmlAssets.Current.Assets[id]).ToArray();
            while (queue.TryDequeue(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen.Count >= input.Packages.Target.Limits.MaxBackbones) throw new FileInspectionLimitException();
                PackageReference? reference = null;
                try { reference = PackageLogicalPath.ResolveReference(item.Source, item.Href); }
                catch (ArgumentException) { }
                if (reference?.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null ||
                    path.Split('/')[0] != document.LogicalPath.Split('/')[0])
                {
                    Add("DELIVERY-ASSETS", CheckStatus.Fail, "STYLESHEET_OUT_OF_SCOPE", "A stylesheet must resolve within the selected sequence without network access.", item.Location, actual: item.Href);
                    continue;
                }
                var exists = _files.TryGetValue(path, out var file);
                _references.Add(new(item.Source, item.Href, reference, item.Location, exists, "Stylesheet"));
                if (!exists) { Add("DELIVERY-ASSETS", CheckStatus.Fail, "STYLESHEET_MISSING", "The referenced stylesheet is not delivered.", item.Location, actual: path); continue; }
                if (!seen.Add(path)) continue;
                var pinned = styles.SingleOrDefault(asset => path == document.LogicalPath.Split('/')[0] + "/" + asset.LogicalPath);
                if (pinned is null)
                    Add("DELIVERY-ASSETS", CheckStatus.NotEvaluated, "STYLESHEET_NOT_PINNED", "The selected profile has no pinned digest for this stylesheet.", Locate(path));
                else if (pinned.Sha256 != file!.Sha256)
                    Add("DELIVERY-ASSETS", CheckStatus.Fail, "STYLESHEET_DIGEST_MISMATCH", "The stylesheet differs from the pinned asset bytes.", Locate(path), pinned.Sha256, file.Sha256);
                if (!path.EndsWith(".xsl", StringComparison.OrdinalIgnoreCase))
                { Add("DELIVERY-ASSETS", CheckStatus.NotEvaluated, "STYLESHEET_FORMAT_UNSUPPORTED", "Only XML stylesheet dependency syntax is inspected.", Locate(path)); continue; }
                try
                {
                    using var stream = input.Packages.OpenRead(path);
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null, MaxCharactersInDocument = input.Packages.Target.Limits.MaxXmlCharacters });
                    var nodes = 0;
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++nodes > input.Packages.Target.Limits.MaxXmlNodes || reader.Depth > input.Packages.Target.Limits.MaxXmlDepth)
                            throw new FileInspectionLimitException();
                        if (reader.NodeType != XmlNodeType.Element) continue;
                        if (reader.NamespaceURI == "http://www.w3.org/1999/XSL/Transform" && reader.LocalName is "import" or "include")
                        {
                            var href = reader.GetAttribute("href");
                            if (string.IsNullOrEmpty(href)) Add("DELIVERY-ASSETS", CheckStatus.Fail, "STYLESHEET_DEPENDENCY_INVALID", "An XSL import/include has no href.", Locate(path));
                            else queue.Enqueue((path, href, Locate(path)));
                        }
                        else if (reader.GetAttribute("href") is not null || reader.GetAttribute("src") is not null)
                            Add("DELIVERY-ASSETS", CheckStatus.NotEvaluated, "STYLESHEET_OUTPUT_RESOURCE_UNEVALUATED", "Stylesheet output resources require evaluation of the intended presentation base; the stylesheet is not executed.", Locate(path));
                    }
                }
                catch (XmlException exception)
                { Add("DELIVERY-ASSETS", CheckStatus.Fail, "STYLESHEET_XML_INVALID", "Stylesheet XML is invalid or attempts prohibited DTD/entity use: " + exception.Message, Locate(path)); }
            }
        }

        private PackageReference? Resolve(string source, string value, ValidationLocation location)
        {
            PackageReference reference;
            try { reference = PackageLogicalPath.ResolveReference(source, value); }
            catch (ArgumentException)
            {
                Add("FILE-HREF", CheckStatus.Fail, "INVALID_FILE_REFERENCE", "The file reference is malformed or escapes the application.", location, actual: value);
                return null;
            }
            if (reference.ExternalUri is not null)
            {
                Add("FILE-HREF", CheckStatus.NotEvaluated, "EXTERNAL_CONTENT_REFERENCE", "External content is classified without fetching it; it cannot establish a delivered document.", location, actual: value);
                return reference;
            }
            var path = reference.LogicalPath!;
            var sequence = path.Split('/')[0];
            if (sequence.Length != 4 || sequence.Any(character => character is < '0' or > '9'))
            {
                Add("FILE-HREF", CheckStatus.Fail, "INVALID_FILE_REFERENCE", "A leaf must identify a file in this application's sequence namespace.", location, actual: value);
                return null;
            }
            if (reference.Query is not null)
                Add("FILE-HREF", CheckStatus.NotEvaluated, "FILE_REFERENCE_QUERY_UNSUPPORTED", "The file part is inspected, but query semantics require an applicable format policy.", location, actual: value);
            if (string.CompareOrdinal(sequence, source.Split('/')[0]) > 0)
            {
                Add("FILE-HREF", CheckStatus.Fail, "FILE_REFERENCE_IN_FUTURE", "A leaf cannot reuse content from a later sequence.", location, actual: value);
                return null;
            }
            if (!input.Packages.Sequences.ContainsKey(sequence))
                Add("FILE-HREF", CheckStatus.NotEvaluated, "FILE_HISTORY_NOT_AVAILABLE", "The referenced historical file source is absent from the explicit baseline.", location, actual: path);
            else if (!_files.ContainsKey(path))
                Add("FILE-HREF", CheckStatus.Fail, "REFERENCED_FILE_MISSING", "The exact referenced file is missing from the selected source.", location, actual: path);
            return reference;
        }

        private async Task CheckIndexAsync(ICapturedPackageInput capture)
        {
            var index = capture.Manifest.SequenceNumber + "/index.xml";
            var checksum = capture.Manifest.SequenceNumber + "/index-md5.txt";
            if (!_files.TryGetValue(index, out var indexFile) || !_files.TryGetValue(checksum, out var checksumFile))
            {
                Add("INDEX-MD5", CheckStatus.Fail, "INDEX_CHECKSUM_FILE_MISSING", "Both index.xml and index-md5.txt must be delivered.", Locate(checksum));
                return;
            }
            if (checksumFile.Length > 128)
            {
                Add("INDEX-MD5", CheckStatus.Fail, "INVALID_INDEX_CHECKSUM", "index-md5.txt must contain one MD5 value.", Locate(checksum));
                return;
            }
            using var stream = capture.OpenRead(checksum);
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
            var value = (await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
            if (!IsMd5(value)) Add("INDEX-MD5", CheckStatus.Fail, "INVALID_INDEX_CHECKSUM", "Expected one 32-digit hexadecimal MD5 value, optionally followed by a line ending.", Locate(checksum), actual: value);
            else if (!string.Equals(value, indexFile.Md5, StringComparison.OrdinalIgnoreCase))
                Add("INDEX-MD5", CheckStatus.Fail, "INDEX_CHECKSUM_MISMATCH", "index-md5.txt differs from the actual index.xml bytes.", Locate(checksum), indexFile.Md5, value);
        }

        private void Inventory(ICapturedPackageInput capture)
        {
            foreach (var directory in capture.Manifest.Directories.Where(directory => directory.IsEmpty))
                Add("DELIVERY-STRUCTURE", CheckStatus.Pass, "EMPTY_DELIVERY_DIRECTORY", "The selected delivery contains an empty directory.", Locate(directory.LogicalPath), severity: ValidationSeverity.Info);
            foreach (var file in capture.Manifest.AdditionalFiles)
                Add("DELIVERY-STRUCTURE", CheckStatus.Pass, "SURROUNDING_INPUT_FILE", "This captured surrounding file is outside the selected sequence and is not adopted as delivery or history.",
                    new(capture.Manifest.SequenceNumber, file.LogicalPath), severity: ValidationSeverity.Info);
            foreach (var path in capture.Manifest.Files.Select(file => (file.LogicalPath, IsFile: true))
                         .Concat(capture.Manifest.Directories.Select(directory => (directory.LogicalPath, IsFile: false))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = path.LogicalPath.Split('/')[^1];
                var components = name.Split('.');
                var valid = path.IsFile ? components.Length == 2 && components.All(IsName) : IsName(name);
                if (!valid || name.Length > 64 || path.LogicalPath.Length > 230)
                    Add("FILE-NAMING", CheckStatus.Fail, "ICH_FILE_NAME_INVALID", "ICH Appendix 2 requires lower-case name tokens, one file extension, names up to 64 characters and paths up to 230 characters including the sequence.",
                        Locate(path.LogicalPath), actual: path.LogicalPath);
            }
        }

        public void Compare()
        {
            if (input.Comparison is null) return;
            var primary = input.Packages.Target.Manifest;
            var other = input.Comparison.Manifest;
            var archive = input.Packages.Target.SourceKind == PackageSourceKind.Zip ? primary : other;
            foreach (var file in archive.AdditionalFiles)
                Add("ZIP-PARITY", CheckStatus.Fail, "ZIP_EXTRA_CONTENT", "The compared final ZIP contains content outside the selected sequence.",
                    new(archive.SequenceNumber, file.LogicalPath));
            foreach (var directory in archive.AdditionalDirectories.Where(directory => directory.IsEmpty))
                Add("ZIP-PARITY", CheckStatus.Fail, "ZIP_EXTRA_CONTENT", "The compared final ZIP contains an empty directory outside the selected sequence.",
                    new(archive.SequenceNumber, directory.LogicalPath));
            var left = primary.Files.ToDictionary(file => file.LogicalPath, StringComparer.Ordinal);
            var right = other.Files.ToDictionary(file => file.LogicalPath, StringComparer.Ordinal);
            foreach (var path in left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!left.TryGetValue(path, out var first) || !right.TryGetValue(path, out var second))
                    Add("ZIP-PARITY", CheckStatus.Fail, "DIRECTORY_ZIP_PATH_MISMATCH", "A file is present in only one explicitly selected delivery.", Locate(path));
                else if (first.Length != second.Length || first.Sha256 != second.Sha256)
                    Add("ZIP-PARITY", CheckStatus.Fail, "DIRECTORY_ZIP_CONTENT_MISMATCH", "The same logical filename has different bytes in the directory and ZIP.", Locate(path), first.Sha256, second.Sha256);
            }
            var leftEmpty = primary.Directories.Where(directory => directory.IsEmpty).Select(directory => directory.LogicalPath).ToHashSet(StringComparer.Ordinal);
            var rightEmpty = other.Directories.Where(directory => directory.IsEmpty).Select(directory => directory.LogicalPath).ToHashSet(StringComparer.Ordinal);
            foreach (var path in leftEmpty.Except(rightEmpty, StringComparer.Ordinal).Concat(rightEmpty.Except(leftEmpty, StringComparer.Ordinal)).Order(StringComparer.Ordinal))
                Add("ZIP-PARITY", CheckStatus.Fail, "DIRECTORY_ZIP_EMPTY_DIRECTORY_MISMATCH", "An empty delivery directory is present in only one input.", Locate(path));
        }

        public PackageFileInspection Finish()
        {
            var checks = RuleIds.Select(rule =>
            {
                var findings = _findings.Where(finding => finding.RuleId == rule).ToArray();
                var status = findings.Any(finding => finding.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail : Incomplete ||
                    findings.Any(finding => finding.CheckStatus == CheckStatus.NotEvaluated) ? CheckStatus.NotEvaluated : CheckStatus.Pass;
                var reason = "Checks use the reverified captured bytes and explicitly selected history.";
                if (rule == "ZIP-PARITY" && input.Comparison is null && !Incomplete)
                {
                    status = input.Packages.Target.SourceKind == PackageSourceKind.Zip ? CheckStatus.NotEvaluated : CheckStatus.NotApplicable;
                    reason = "No corresponding directory/ZIP was explicitly supplied for parity comparison.";
                }
                return new RuleCheckResult(rule, status, reason, findings);
            });
            return new(input, lifecycle, _references, checks);
        }

        private static bool IsMd5(string? value) => value is { Length: 32 } && value.All(Uri.IsHexDigit);
        private static bool IsName(string value) => value.Length > 0 && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
        private static ValidationLocation Locate(string path) => new(path.Split('/')[0], path);
    }
}
