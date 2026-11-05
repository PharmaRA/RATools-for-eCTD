namespace RATools.Application.Applications;

// The budget is per invocation and charged before accepting the next source.
// Failed sequence attempts remain charged: parsing failures must not let a large
// workspace evade the total input bound by repeatedly discarding source arrays.
internal sealed class ImportEvidenceBudget
{
    internal const int MaximumSequences = 10000;
    internal const int MaximumSources = 20000;
    internal const long MaximumBytes = 128 * 1024 * 1024;
    private int _sequences;
    private int _sources;
    private long _bytes;
    internal void Sequence()
    {
        if (_sequences >= MaximumSequences) throw Exceeded();
        _sequences++;
    }
    internal void Source(long length)
    {
        if (_sources >= MaximumSources) throw Exceeded();
        Bytes(length); _sources++;
    }
    internal void Bytes(long count)
    {
        if (count < 0 || count > MaximumBytes - _bytes) throw Exceeded();
        _bytes += count;
    }
    private static InvalidOperationException Exceeded() => new("IMPORT_EVIDENCE_BUDGET_EXCEEDED: XML sources exceed the per-import byte or item budget before persistence.");
}
