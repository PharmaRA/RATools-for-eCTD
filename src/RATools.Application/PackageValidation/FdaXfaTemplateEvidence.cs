using System.Text.Json;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Application.PackageValidation;

// This proves template-byte equality only, not main-form identity, field meaning,
// rendered values, fillability, historical applicability or regulatory compliance.
public sealed record PackageFormTemplateMatch(string LogicalPath, string CapturedSha256, string StandardsSnapshotId,
    string SourceFormId, string SourcePdfSha256, string TemplateSha256, string AlgorithmVersion);

internal static class FdaXfaTemplateEvidence
{
    internal const string AlgorithmVersion = "fda-xfa-template-equality-v1";
    internal const string InventoryHash = "d6784bfd301bd384865e81539857e2d5a4efdf837ac6a27903f956b236796e32";
    internal sealed record Template(string FormId, string SourceSha256, string TemplateSha256);

    internal static IReadOnlyList<Template> ReadCatalog(StandardsProfileSnapshot snapshot)
    {
        if (snapshot.TemplateKey != "us-fda-ectd-3.2.2" || snapshot.Descriptor.Region != "United States" ||
            snapshot.Descriptor.CriteriaVersion != "4.7" || snapshot.Descriptor.RegionalModule1Version != "3.3") return [];
        var inventory = snapshot.Resources.SingleOrDefault(item => item.Key == "official-fda-form-inventory");
        if (inventory is null || inventory.Category != "SourceInventory" || inventory.MediaType != "application/json" ||
            inventory.RelativePath != "metadata/official-sources/forms-2026-11-20.json" || inventory.Sha256 != InventoryHash) return [];
        using var document = JsonDocument.Parse(inventory.ReadBytes());
        var templates = new List<Template>();
        foreach (var form in document.RootElement.GetProperty("sources").EnumerateArray())
        {
            if (!form.TryGetProperty("decodedTemplateSha256", out var template)) continue;
            var id = form.GetProperty("id").GetString()!;
            var hash = form.GetProperty("sha256").GetString()!;
            var original = snapshot.Resources.SingleOrDefault(item => item.Key == "official-" + id);
            if (original is null || original.Category != "OfficialSource" || original.MediaType != "application/pdf" ||
                original.RelativePath != "official-sources/" + id + ".pdf" || original.Sha256 != hash ||
                original.Length != form.GetProperty("bytes").GetInt64()) continue;
            if (!snapshot.Descriptor.Provenance.Any(item => item.Sha256 == hash && item.VerificationStatus == "Verified" &&
                item.SourceUrl == form.GetProperty("requestedUrl").GetString() &&
                item.Version == "Archived PDF modification " + form.GetProperty("pdfModificationDate").GetString() &&
                item.AcquiredOn == new DateOnly(2026, 11, 20) && item.VerifiedOn == new DateOnly(2026, 11, 26))) continue;
            templates.Add(new(id, hash, template.GetString()!));
        }
        return Array.AsReadOnly(templates.ToArray());
    }

    internal sealed class Inspection
    {
        private readonly PackageReadLimits _limits;
        private readonly CancellationToken _token;
        private readonly string? _snapshotId;
        private readonly IReadOnlyList<Template> _templates;
        private readonly Dictionary<string, PackageInputFile> _expected = new(StringComparer.Ordinal);
        private readonly HashSet<string> _observed = new(StringComparer.Ordinal);
        private readonly List<PackageFormTemplateMatch> _matches = [];
        private int _work;
        private bool _ready;
        internal IReadOnlyList<PackageFormTemplateMatch> Matches => _matches.AsReadOnly();

        internal Inspection(PackageDeliveryInput input, PackageFileInspection files, PackageStandardsContext context, CancellationToken token)
        {
            _limits = input.Packages.Target.Limits; _token = token; _work = _limits.MaxPdfObjects;
            _snapshotId = context.SnapshotId; _templates = context.FdaXfaTemplates;
            if (_snapshotId is null || _templates.Count == 0 || files.InputDigest != input.InputDigest ||
                files.HistoryManifestDigest != input.Packages.Baseline.Digest || files.RulesDigest != context.Catalog.Digest ||
                files.AssetManifestDigest != context.Assets.Digest || files.LimitsDigest != _limits.Digest()) return;
            foreach (var file in input.Packages.Target.Manifest.Files)
            {
                token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                if (file.LogicalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !_expected.TryAdd(file.LogicalPath, file)) return;
            }
            _ready = true;
        }

        internal void Observe(PackagePdfDocument document)
        {
            _token.ThrowIfCancellationRequested(); if (!_ready || _work-- <= 0) return;
            if (!_expected.TryGetValue(document.LogicalPath, out var captured) || !_observed.Add(document.LogicalPath) ||
                document.CapturedSha256 != captured.Sha256 || document.Facts is not { } facts ||
                facts.ContentProcessing != PdfContentProcessingStatus.Processed || facts.Basics.ParseError is not null ||
                facts.Basics.PageCount is null or < 1 || facts.Form.HasAcroForm != true || !facts.Form.HasXfa ||
                !facts.Form.Xfa.ReadComplete || facts.Form.Xfa.IncompleteReasons.Count != 0) return;
            // Official XFA forms can retain permission encryption while opening normally.
            // Successful processing and complete decoded packets establish byte evidence;
            // they do not establish security-policy compliance or fillability.
            var names = new HashSet<string>(StringComparer.Ordinal); PackageXfaPacket? template = null; var datasets = false; long bytes = 0;
            foreach (var packet in facts.Form.Xfa.Packets)
            {
                _token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                if (string.IsNullOrWhiteSpace(packet.Name) || !names.Add(packet.Name) || packet.Name == "xdp" ||
                    packet.DecodedBytes <= 0 || packet.DecodedBytes > _limits.MaxXmlCharacters - bytes ||
                    packet.DecodedBytes > _limits.MaxExpandedBytes - bytes || !Digest(packet.Sha256)) return;
                bytes += packet.DecodedBytes;
                if (packet.Name == "template") template = packet;
                if (packet.Name == "datasets") datasets = true;
            }
            if (template is null || !datasets) return;
            Template? match = null;
            foreach (var candidate in _templates)
            {
                _token.ThrowIfCancellationRequested(); if (_work-- <= 0) return;
                if (candidate.TemplateSha256 != template.Sha256) continue;
                if (match is not null) return;
                match = candidate;
            }
            if (match is null) return;
            _token.ThrowIfCancellationRequested();
            _matches.Add(new(document.LogicalPath, captured.Sha256, _snapshotId!, match.FormId, match.SourceSha256,
                template.Sha256, AlgorithmVersion));
        }

        private static bool Digest(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
