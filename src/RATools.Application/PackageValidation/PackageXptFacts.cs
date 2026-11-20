namespace RATools.Application.PackageValidation;

internal enum XptReadStatus { Read, Invalid, Unsupported, InputMismatch, LimitExceeded }
internal enum XptTextEncoding { AsciiOnly, Utf8 }
internal sealed record XptReadLimits(int MaxBytes = 8 * 1024 * 1024, int MaxVariables = 1024,
    int MaxRecordBytes = 1024 * 1024, int MaxRows = 100000, int MaxCells = 250000);
internal sealed record XptVariable(string Name, string Label, int Type, int Length, int Position, int Number, long DescriptorOffset);
internal sealed record XptCell(string RawHex, string? Text, double? Number, string? MissingCode, bool Decoded);
internal sealed class XptRow(int index, long offset, bool certain, IEnumerable<XptCell> cells)
{
    internal int Index { get; } = index;
    internal long Offset { get; } = offset;
    internal bool Certain { get; } = certain;
    internal IReadOnlyList<XptCell> Cells { get; } = Array.AsReadOnly(cells.ToArray());
}

// Content observations only; no CDISC qualification, study applicability or FDA rule result.
internal sealed class PackageXptFacts(XptReadStatus status, string reason, string? sha256, XptTextEncoding encoding,
    bool structureComplete = false, bool valuesComplete = false, string? memberName = null, string? memberLabel = null,
    int? recordBytes = null, int? minPossibleRows = null, int? maxPossibleRows = null,
    IEnumerable<XptVariable>? variables = null, IEnumerable<XptRow>? rows = null)
{
    internal XptReadStatus Status { get; } = status;
    internal string Reason { get; } = reason;
    internal string? Sha256 { get; } = sha256;
    internal XptTextEncoding Encoding { get; } = encoding;
    internal bool StructureComplete { get; } = structureComplete;
    internal bool ValuesComplete { get; } = valuesComplete;
    internal string? MemberName { get; } = memberName;
    internal string? MemberLabel { get; } = memberLabel;
    internal int? RecordBytes { get; } = recordBytes;
    internal int? MinPossibleRows { get; } = minPossibleRows;
    internal int? MaxPossibleRows { get; } = maxPossibleRows;
    internal IReadOnlyList<XptVariable> Variables { get; } = Array.AsReadOnly((variables ?? []).ToArray());
    internal IReadOnlyList<XptRow> Rows { get; } = Array.AsReadOnly((rows ?? []).ToArray());
}
