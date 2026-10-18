using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Domain.PackageValidation;

// One difference between an external validation tool's report and this engine.
// Every difference must carry an explicit explanation; registration rejects an
// unexplained difference instead of silently accepting it.
public sealed record ExternalComparisonDifference(string ExternalRuleId, string? InternalRuleId,
    string ExternalMessage, string Explanation);

// External evidence is append-only and bound to the exact run binding. Registering a
// report never rewrites the archived validation report; it records that a tool/version
// actually validated the same package bytes and that differences were reviewed.
public sealed class ExternalComparisonReport
{
    public ExternalComparisonReport(Guid runId, ValidationBinding binding, string toolName, string toolVersion,
        string packageDigest, string reportText, IEnumerable<ExternalComparisonDifference> differences,
        DateTime recordedUtc)
    {
        if (runId == Guid.Empty) throw new ArgumentException("A completed validation run is required.", nameof(runId));
        ArgumentNullException.ThrowIfNull(binding);
        RequireText(toolName, 128, nameof(toolName));
        RequireText(toolVersion, 64, nameof(toolVersion));
        RequireDigest(packageDigest, nameof(packageDigest));
        if (!string.Equals(packageDigest, binding.InputManifestDigest, StringComparison.Ordinal))
            throw new ArgumentException("The external report must bind the exact package digest of this run.", nameof(packageDigest));
        if (string.IsNullOrWhiteSpace(reportText))
            throw new ArgumentException("An external report must include its actual report content.", nameof(reportText));
        if (reportText.Length > 8 * 1024 * 1024)
            throw new ArgumentException("The external report content exceeds the supported evidence size.", nameof(reportText));
        if (recordedUtc == default || recordedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("An explicit UTC timestamp is required.", nameof(recordedUtc));
        var copy = differences?.ToArray() ?? throw new ArgumentNullException(nameof(differences));
        foreach (var difference in copy)
        {
            if (difference is null) throw new ArgumentException("Differences cannot contain null entries.", nameof(differences));
            RequireText(difference.ExternalRuleId, 256, nameof(differences));
            RequireText(difference.ExternalMessage, 2048, nameof(differences));
            RequireText(difference.Explanation, 2048, nameof(differences));
            if (difference.InternalRuleId is { Length: > 128 })
                throw new ArgumentException("An internal rule id is too long.", nameof(differences));
        }
        Id = Guid.NewGuid();
        RunId = runId;
        Binding = binding;
        ToolName = toolName.Trim();
        ToolVersion = toolVersion.Trim();
        PackageDigest = packageDigest;
        ReportText = reportText;
        ReportSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reportText))).ToLowerInvariant();
        Differences = Array.AsReadOnly(copy);
        RecordedUtc = recordedUtc;
        Digest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, runId = runId.ToString("D"), binding, toolName = ToolName, toolVersion = ToolVersion,
            packageDigest, reportSha256 = ReportSha256,
            differences = copy.Select(item => new
            {
                externalRuleId = item.ExternalRuleId, internalRuleId = item.InternalRuleId,
                externalMessage = item.ExternalMessage, explanation = item.Explanation
            }).ToArray(),
            recordedUtc
        }));
    }

    public Guid Id { get; }
    public Guid RunId { get; }
    public ValidationBinding Binding { get; }
    public string ToolName { get; }
    public string ToolVersion { get; }
    public string PackageDigest { get; }
    public string ReportText { get; }
    public string ReportSha256 { get; }
    public IReadOnlyList<ExternalComparisonDifference> Differences { get; }
    public DateTime RecordedUtc { get; }
    // The digest of the complete external evidence, including the exact run binding.
    public string Digest { get; }

    private static void RequireText(string value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximum)
            throw new ArgumentException($"Use trimmed, non-empty text of at most {maximum} characters.", name);
    }

    private static void RequireDigest(string value, string name)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A digest must contain 64 lowercase hexadecimal characters.", name);
    }
}

public sealed class ExternalComparisonEvidenceConflictException(Guid runId)
    : InvalidOperationException($"Validation run '{runId}' already has different external comparison evidence.");
