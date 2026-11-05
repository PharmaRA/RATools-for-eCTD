using System.Globalization;

namespace RATools.Application.DocumentProcessing;

public sealed record PdfRemoteTarget(string File, int? PageNumber, string? NamedDestination)
{
    public static PdfRemoteTarget Parse(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var split = target.Split('#', 2);
        var file = split[0];
        var fragment = split.Length == 1 ? "" : split[1];
        if (fragment.Length == 0) return new(file, 1, null);
        if (fragment.StartsWith("page=", StringComparison.Ordinal))
        {
            if (!int.TryParse(fragment.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page < 1)
                throw new PdfProcessingRejectedException("Remote PDF page must be a positive integer.");
            return new(file, page, null);
        }
        var name = fragment.StartsWith("nameddest=", StringComparison.Ordinal) ? fragment[10..] : fragment;
        if (string.IsNullOrWhiteSpace(name)) throw new PdfProcessingRejectedException("Remote PDF named destination is empty.");
        return new(file, null, name);
    }
}
