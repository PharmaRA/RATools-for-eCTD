using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed record PackageInputSelection(Guid ApplicationId, string SourceId, string Path,
    string? SequenceNumber = null, string? RootRelativePath = null, bool CaptureSourceLayout = true)
{
    public static PackageInputSelection FromRegistered(RegisteredPackageInput input)
    {
        using var json = System.Text.Json.JsonDocument.Parse(input.ManifestJson);
        var version = json.RootElement.TryGetProperty("capture", out var capture) && capture.TryGetProperty("schemaVersion", out var value) ? value.GetInt32() : 1;
        if (version is not (1 or 2)) throw new InvalidDataException("Unsupported input capture metadata version.");
        if (version == 1 && capture.ValueKind == System.Text.Json.JsonValueKind.Object && capture.TryGetProperty("sourceLayout", out var layout) && layout.ValueKind != System.Text.Json.JsonValueKind.Null)
            throw new InvalidDataException("Legacy input metadata cannot authorize unbound directory facts.");
        return new(input.ApplicationId, input.Id.ToString("D"), input.SourcePath, input.SequenceNumber, input.RootRelativePath, version == 2);
    }
}

public interface IPackageInputReader
{
    Task<ICapturedPackageInput> ReadAsync(PackageInputSelection selection, PackageReadLimits limits, CancellationToken cancellationToken = default);
}

public interface ICapturedPackageInput : IAsyncDisposable
{
    string SourceId { get; }
    PackageSourceKind SourceKind { get; }
    PackageInputManifest Manifest { get; }
    string? ArchiveSha256 { get; }
    PackageSourceLayout? SourceLayout => null;
    string InputDigest { get; }
    PackageReadLimits Limits { get; }
    Stream OpenRead(string logicalPath);
    Stream OpenAdditionalRead(string relativePath) => throw new NotSupportedException("This capture does not expose surrounding-file bytes.");
    Task VerifyUnchangedAsync(CancellationToken cancellationToken = default);
}

public class PackageInputException(string ruleId, string code, string message, string? logicalPath = null, Exception? innerException = null)
    : IOException(message, innerException)
{
    public string RuleId { get; } = ruleId;
    public string Code { get; } = code;
    public string? LogicalPath { get; } = logicalPath;
    public ValidationFinding ToFinding() => new(RuleId, CheckStatus.Fail, ValidationSeverity.Error, Code, Message, new(LogicalPath: LogicalPath));
}

public sealed class PackageSelectionRequiredException(IReadOnlyList<PackageSequenceCandidate> candidates)
    : PackageInputException("INPUT-LAYOUT", "SEQUENCE_SELECTION_REQUIRED", "Select one explicit sequence root from the available candidates.")
{
    public IReadOnlyList<PackageSequenceCandidate> Candidates { get; } = candidates;
}

public sealed class PackageInputChangedException(string sourceId, Exception? innerException = null)
    : PackageInputException("INPUT-STABLE", "INPUT_CHANGED", $"Input '{sourceId}' changed after capture; this run cannot authorize finalization.", innerException: innerException);
