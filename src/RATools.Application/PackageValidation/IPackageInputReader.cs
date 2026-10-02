using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed record PackageInputSelection(Guid ApplicationId, string SourceId, string Path,
    string? SequenceNumber = null, string? RootRelativePath = null);

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
    string InputDigest { get; }
    PackageReadLimits Limits { get; }
    Stream OpenRead(string logicalPath);
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

public sealed class PackageInputChangedException(string sourceId)
    : PackageInputException("INPUT-STABLE", "INPUT_CHANGED", $"Input '{sourceId}' changed after capture; this run cannot authorize finalization.");
