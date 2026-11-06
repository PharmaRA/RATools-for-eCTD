using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Domain.PackageValidation;
using RATools.Application.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

internal static class PackageValidationJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException("A persisted validation value is null.");
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string VerifyReport(string json, string expectedHash, ValidationBinding binding)
    {
        if (Hash(json) != expectedHash) throw new InvalidDataException("The archived validation report digest differs from its committed run.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
            Read<ValidationBinding>(document.RootElement.GetProperty("binding").GetRawText()) != binding)
            throw new InvalidDataException("The archived report schema or binding differs from its run.");
        return json;
    }

    internal static ValidationFinding[] ReportFindings(string verifiedReportJson)
    {
        using var document = JsonDocument.Parse(verifiedReportJson);
        return document.RootElement.GetProperty("coverage").EnumerateArray()
            .SelectMany(rule => rule.GetProperty("findings").EnumerateArray())
            .Select(finding => Read<ValidationFinding>(finding.GetRawText())).ToArray();
    }

    internal static bool Matches(ValidationFinding finding, PackageFindingQuery query, IReadOnlyCollection<string>? ruleIds) =>
        (query.RuleId is null || finding.RuleId == query.RuleId) &&
        (query.Severity is null || finding.Severity == query.Severity) &&
        (query.Status is null || finding.CheckStatus == query.Status) &&
        (query.LogicalPath is null || finding.Location.LogicalPath == query.LogicalPath) &&
        (query.NodePath is null || finding.Location.NodePath?.Contains(query.NodePath, StringComparison.Ordinal) == true) &&
        (ruleIds is null || ruleIds.Contains(finding.RuleId, StringComparer.Ordinal));

    internal static void VerifyFinding(string json, ValidationFinding expected)
    {
        try
        {
            // Older archives can omit optional null properties or use other JSON
            // whitespace. Compare the defined evidence fields, then serve the report.
            if (Write(Read<ValidationFinding>(json)) != Write(expected))
                throw new InvalidDataException("A finding's content differs from the committed report.");
        }
        catch (JsonException error) { throw new InvalidDataException("The stored finding JSON is malformed.", error); }
    }

    internal static string VerifyProjection(string json, PackageValidationRun run)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (run.Status != ValidationRunStatus.Completed || run.ReportSha256 is null ||
                root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("applicationId").GetGuid() != run.ApplicationId ||
                root.GetProperty("targetSequence").GetString() != run.SequenceNumber ||
                root.GetProperty("inputDigest").GetString() != run.Binding.InputManifestDigest ||
                root.GetProperty("baselineDigest").GetString() != run.Binding.HistoryBaselineDigest ||
                root.GetProperty("profileSnapshotId").GetString() != run.Binding.ProfileSnapshotId ||
                root.GetProperty("limitsDigest").GetString() != run.Binding.ResourceLimitsDigest)
                throw new InvalidDataException("The archived projection identity or binding differs from its completed run.");
            return json;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("The archived projection is malformed.", error); }
    }
}
