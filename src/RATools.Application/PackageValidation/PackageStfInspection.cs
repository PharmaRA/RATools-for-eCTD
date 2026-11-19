using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed class ParsedStudyTaggingFile(string logicalPath, string? capturedSha256, bool readComplete,
    CheckStatus dtdStatus, string? systemIdentifier, IEnumerable<ParsedXmlElement> elements,
    IEnumerable<ParsedXmlProcessingInstruction> processingInstructions, IEnumerable<string> resolvedAssets)
{
    public string LogicalPath { get; } = logicalPath;
    public string? CapturedSha256 { get; } = capturedSha256;
    public bool ReadComplete { get; } = readComplete;
    public CheckStatus DtdStatus { get; } = dtdStatus;
    public string? SystemIdentifier { get; } = systemIdentifier;
    public IReadOnlyList<ParsedXmlElement> Elements { get; } = Array.AsReadOnly(elements.ToArray());
    public IReadOnlyList<ParsedXmlProcessingInstruction> ProcessingInstructions { get; } = Array.AsReadOnly(processingInstructions.ToArray());
    public IReadOnlyList<string> ResolvedAssets { get; } = Array.AsReadOnly(resolvedAssets.ToArray());
}

// Discovery/DTD facts, not STF business-rule acceptance or evidence that all studies have STFs.
public sealed class PackageStfInspection(bool inventoryComplete, IEnumerable<ParsedStudyTaggingFile> documents,
    IEnumerable<ValidationFinding>? findings = null)
{
    public bool InventoryComplete { get; } = inventoryComplete;
    public IReadOnlyList<ParsedStudyTaggingFile> Documents { get; } = Array.AsReadOnly(documents.ToArray());
    public IReadOnlyList<ValidationFinding> Findings { get; } = Array.AsReadOnly((findings ?? []).ToArray());
    public static PackageStfInspection Unknown { get; } = new(false, []);
}
