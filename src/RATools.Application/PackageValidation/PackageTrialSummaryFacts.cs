namespace RATools.Application.PackageValidation;

internal enum TrialSummaryStatus { Read, Unavailable, NotTrialSummary, InvalidShape, LimitExceeded }
internal sealed record TrialSummaryRow(int Index, long Offset, bool Certain, string? StudyId, string? Parameter,
    string? Value, string? NullFlavor, bool ValuesDecoded, XptRow Original);
internal sealed class PackageTrialSummaryFacts(TrialSummaryStatus status, CapturedXptFacts capture, bool structureComplete = false,
    bool requiredValuesDecoded = false, bool hasNullFlavorColumn = false, int? minPossibleRows = null, int? maxPossibleRows = null,
    IEnumerable<TrialSummaryRow>? rows = null)
{
    internal TrialSummaryStatus Status { get; } = status;
    internal CapturedXptFacts Capture { get; } = capture;
    internal bool StructureComplete { get; } = structureComplete;
    internal bool RequiredValuesDecoded { get; } = requiredValuesDecoded;
    internal bool HasNullFlavorColumn { get; } = hasNullFlavorColumn;
    internal int? MinPossibleRows { get; } = minPossibleRows;
    internal int? MaxPossibleRows { get; } = maxPossibleRows;
    internal IReadOnlyList<TrialSummaryRow> Rows { get; } = Array.AsReadOnly((rows ?? []).ToArray());
}

internal static class PackageTrialSummaryReader
{
    internal static PackageTrialSummaryFacts Read(CapturedXptFacts capture, int maxRows = 100000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(maxRows);
        if (capture.Status != CapturedXptStatus.Read || capture.Content is not { Status: XptReadStatus.Read, StructureComplete: true } content ||
            content.MinPossibleRows is not { } minimum || content.MaxPossibleRows is not { } maximum)
            return new(TrialSummaryStatus.Unavailable, capture);
        if (!string.Equals(content.MemberName, "TS", StringComparison.OrdinalIgnoreCase))
            return new(TrialSummaryStatus.NotTrialSummary, capture);
        var fields = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < content.Variables.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!fields.TryAdd(content.Variables[i].Name, i)) return new(TrialSummaryStatus.InvalidShape, capture);
        }
        if (!fields.TryGetValue("STUDYID", out var study) || !fields.TryGetValue("TSPARMCD", out var parameter) ||
            !fields.TryGetValue("TSVAL", out var value)) return new(TrialSummaryStatus.InvalidShape, capture);
        var hasNullFlavor = fields.TryGetValue("TSVALNF", out var nullFlavor);
        if (content.Variables[study].Type != 2 || content.Variables[parameter].Type != 2 || content.Variables[value].Type != 2 ||
            hasNullFlavor && content.Variables[nullFlavor].Type != 2 || minimum < 0 || minimum > maximum || content.Rows.Count != maximum)
            return new(TrialSummaryStatus.InvalidShape, capture);
        if (maximum > maxRows) return new(TrialSummaryStatus.LimitExceeded, capture);
        var rows = new List<TrialSummaryRow>();
        var decoded = true;
        foreach (var row in content.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Index != rows.Count || row.Certain != (row.Index < minimum) || row.Cells.Count != content.Variables.Count)
                return new(TrialSummaryStatus.InvalidShape, capture);
            var studyCell = row.Cells[study];
            var parameterCell = row.Cells[parameter];
            var valueCell = row.Cells[value];
            var nullCell = hasNullFlavor ? row.Cells[nullFlavor] : null;
            var requiredDecoded = studyCell.Decoded && studyCell.Text is not null && parameterCell.Decoded && parameterCell.Text is not null &&
                valueCell.Decoded && valueCell.Text is not null;
            var rowDecoded = requiredDecoded && (!hasNullFlavor || nullCell is { Decoded: true, Text: not null });
            decoded &= requiredDecoded;
            rows.Add(new(row.Index, row.Offset, row.Certain, studyCell.Decoded ? studyCell.Text : null,
                parameterCell.Decoded ? parameterCell.Text : null, valueCell.Decoded ? valueCell.Text : null,
                nullCell is { Decoded: true } ? nullCell.Text : null, rowDecoded, row));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(TrialSummaryStatus.Read, capture, true, decoded, hasNullFlavor, minimum, maximum, rows);
    }
}
