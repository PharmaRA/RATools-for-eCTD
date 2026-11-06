namespace RATools.Infrastructure.DocumentProcessing;

// A slot covers a running tool process tree until exit and output-reader cleanup.
// Python's existing Job Object separately limits each tree to four processes.
public sealed class PdfToolProcessBudget : IDisposable
{
    internal static PdfToolProcessBudget Shared { get; } = new(2);
    private readonly SemaphoreSlim _slots;

    public PdfToolProcessBudget(int maxConcurrentProcesses = 2)
    {
        if (maxConcurrentProcesses is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maxConcurrentProcesses));
        _slots = new(maxConcurrentProcesses, maxConcurrentProcesses);
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_slots);
    }

    public void Dispose() => _slots.Dispose();

    private sealed class Lease(SemaphoreSlim slots) : IAsyncDisposable
    {
        private SemaphoreSlim? _slots = slots;
        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _slots, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
