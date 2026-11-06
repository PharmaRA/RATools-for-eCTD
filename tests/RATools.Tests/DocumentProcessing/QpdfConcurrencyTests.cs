using RATools.Infrastructure.DocumentProcessing;

namespace RATools.Tests.DocumentProcessing;

public sealed class QpdfConcurrencyTests
{
    [Fact]
    public async Task StderrFailureDrainsThePendingOutputWriteBeforeDisposingStreamsOrReleasingTheSlot()
    {
        var directory = Path.GetFullPath(".artifacts/qa-04-20261106/r06-readers/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var trigger = Path.Combine(directory, "emit-stderr.txt");
        const string helper = """
            import sys,time
            from pathlib import Path
            sys.stdout.buffer.write(b'x'); sys.stdout.buffer.flush()
            while not Path(sys.argv[1]).exists(): time.sleep(.01)
            sys.stderr.write('e'*8192); sys.stderr.flush()
            time.sleep(30)
            """;
        using var budget = new PdfToolProcessBudget(1);
        using var output = new PendingWriteStream();
        var processId = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        {
            ExecutablePath = "C:/Program Files/Python311/python.exe", ExecutableArguments = ["-I", "-B", "-c", helper, trigger],
            OutputStreamFactory = _ => output, ProcessStartedCallback = id => processId.TrySetResult(id)
        }, budget);
        var method = typeof(QpdfProcessingEngine).GetMethod("RunToFileAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var execution = (Task)method.Invoke(engine, [Array.Empty<string>(), Path.Combine(directory, "output.pdf"),
            TimeSpan.FromSeconds(10), 1024L, CancellationToken.None])!;
        Task<IAsyncDisposable>? waiter = null;
        using var waitCancellation = new CancellationTokenSource();
        try
        {
            await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var process = System.Diagnostics.Process.GetProcessById(await processId.Task);
            await File.WriteAllTextAsync(trigger, "emit");
            using var exitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(exitDeadline.Token);
            await Task.WhenAny(execution, Task.Delay(250));
            Assert.False(execution.IsCompleted, "The pending stdout write must finish before the run completes.");
            Assert.False(output.Disposed);
            waiter = budget.AcquireAsync(waitCancellation.Token).AsTask();
            Assert.False(waiter.IsCompleted, "The process slot must include reader cleanup.");
        }
        finally
        {
            waitCancellation.Cancel(); output.Release.TrySetResult();
            await execution;
            await output.WriteFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (waiter is not null)
            {
                try { await using var acquired = await waiter; }
                catch (OperationCanceledException) { }
            }
        }
        Assert.True(output.Disposed);
        await using var recovered = await budget.AcquireAsync();
    }

    private sealed class PendingWriteStream : MemoryStream
    {
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult();
            try
            {
                // Model an outstanding write that cannot be cancelled immediately.
                await Release.Task;
                throw new IOException("Injected late output-write failure.");
            }
            finally { WriteFinished.TrySetResult(); }
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task QpdfCpuLimitEndsTheOwnedToolAndReleasesItsBudgetSlot()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var budget = new PdfToolProcessBudget(1);
        var directory = Path.GetFullPath(".artifacts/qa-04-20261106/r06-cpu/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.GetFullPath("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        {
            ExecutablePath = "C:/Program Files/Python311/python.exe", ExecutableArguments = ["-I", "-B", "-c", "while True: pass"],
            MaxCpuTime = TimeSpan.FromSeconds(1)
        }, budget);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await engine.ExecuteAsync(new(source, Path.Combine(directory, "unused.pdf"), new(null, null, null),
            TimeSpan.FromSeconds(12), 65536, 65536));
        Assert.False(result.Succeeded);
        Assert.NotEqual("ENGINE_TIMEOUT", result.ErrorMessage);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var normal = new QpdfProcessingEngine(new QpdfProcessingOptions
        { ExecutablePath = Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe") }, budget);
        Assert.Equal(2, (await normal.InspectAsync(source, 8 * 1024 * 1024, deadline.Token)).PageCount);
    }

    [Fact]
    public async Task QpdfHostEnforcesTheConfiguredJobMemoryLimitBeforeExecutingToolCode()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.GetFullPath(".artifacts/qa-04-20261106/r06-memory/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var record = Path.Combine(directory, "allocated.txt");
        const string helper = """
            import ctypes,sys
            from ctypes import wintypes as w
            K=ctypes.WinDLL('kernel32',use_last_error=True)
            K.VirtualAlloc.argtypes=[ctypes.c_void_p,ctypes.c_size_t,w.DWORD,w.DWORD]; K.VirtualAlloc.restype=ctypes.c_void_p
            blocks=[]
            for i in range(32):
             block=K.VirtualAlloc(None,8*1024*1024,0x3000,0x04)
             if not block: break
             blocks.append(block)
            open(sys.argv[1],'w').write(str(len(blocks)))
            print('controlled allocation helper')
            """;
        using var budget = new PdfToolProcessBudget(1);
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        {
            ExecutablePath = "C:/Program Files/Python311/python.exe", ExecutableArguments = ["-I", "-B", "-c", helper, record],
            MaxMemoryBytes = 64 * 1024 * 1024
        }, budget);
        await engine.ExecuteAsync(new(Path.GetFullPath("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf"),
            Path.Combine(directory, "unused.pdf"), new(null, null, null), TimeSpan.FromSeconds(10), 65536, 65536));

        Assert.InRange(int.Parse(await File.ReadAllTextAsync(record), System.Globalization.CultureInfo.InvariantCulture), 1, 7);
    }

    [Fact]
    public async Task PythonCapabilitiesShareTheProcessBudgetAndRecoverAfterCancelledWaiting()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var budget = new PdfToolProcessBudget(1);
        var held = await budget.AcquireAsync();
        var script = Path.GetFullPath("scripts/pdf_content_worker.py");
        using var engine = new PythonPdfContentEngine(new PdfContentEngineOptions
        {
            Enabled = true, PythonExecutablePath = "C:/Program Files/Python311/python.exe", ScriptPath = script,
            ScriptSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(script))).ToLowerInvariant(),
            ScratchDirectory = Path.GetFullPath(".artifacts/qa-04-20261106/r06-content/" + Guid.NewGuid().ToString("N"))
        }, new QpdfProcessingOptions
        {
            ExecutablePath = Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe")
        }, budget);
        try
        {
            using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.GetCapabilitiesAsync(cancelled.Token));
        }
        finally { await held.DisposeAsync(); }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Assert.True((await engine.GetCapabilitiesAsync(deadline.Token)).Enabled);
    }

    [Fact]
    public async Task InspectAndExecuteShareTheInjectedBudgetAcrossEngineInstances()
    {
        using var budget = new PdfToolProcessBudget(1);
        using var release = new ManualResetEventSlim();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        QpdfProcessingOptions Options() => new()
        {
            ExecutablePath = Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe"),
            ProcessStartedCallback = _ =>
            {
                Interlocked.Increment(ref starts); firstStarted.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Test process barrier expired.");
            }
        };
        var inspector = new QpdfProcessingEngine(Options(), budget);
        var executor = new QpdfProcessingEngine(Options(), budget);
        var source = Path.GetFullPath("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var first = Task.Factory.StartNew(() => inspector.InspectAsync(source, 8 * 1024 * 1024), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        using var cancelled = new CancellationTokenSource();
        Task? queued = null;
        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            queued = executor.ExecuteAsync(new(source, Path.GetFullPath(".artifacts/qa-04-20261106/r06-output/" + Guid.NewGuid().ToString("N") + "/cancelled.pdf"),
                new(null, null, null), TimeSpan.FromSeconds(10), 8 * 1024 * 1024, 1024 * 1024), cancelled.Token);
            cancelled.CancelAfter(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(1, starts);
        }
        finally { cancelled.Cancel(); release.Set(); await first; }
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task StartFailureAndCallbackFailureReleaseTheOnlyProcessSlot()
    {
        using var budget = new PdfToolProcessBudget(1);
        var source = Path.GetFullPath("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var missing = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = "ratools-no-such-qpdf-executable" }, budget);
        await Assert.ThrowsAnyAsync<Exception>(() => missing.InspectAsync(source, 8 * 1024 * 1024));
        var path = Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        var broken = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = path,
            ProcessStartedCallback = _ => throw new IOException("Injected callback failure.") }, budget);
        await Assert.ThrowsAsync<IOException>(() => broken.InspectAsync(source, 8 * 1024 * 1024));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var valid = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = path }, budget);
        Assert.Equal(2, (await valid.InspectAsync(source, 8 * 1024 * 1024, deadline.Token)).PageCount);
    }

    [Fact]
    public async Task DefaultProcessBudgetQueuesTheThirdInspectionAndCancellationNeverStartsIt()
    {
        using var release = new ManualResetEventSlim();
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        {
            ExecutablePath = Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe"),
            ProcessStartedCallback = _ =>
            {
                if (Interlocked.Increment(ref started) == 2) twoStarted.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Test process barrier expired.");
            }
        });
        var source = Path.GetFullPath("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var first = Task.Factory.StartNew(() => engine.InspectAsync(source, 8 * 1024 * 1024), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        var second = Task.Factory.StartNew(() => engine.InspectAsync(source, 8 * 1024 * 1024), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        using var cancelled = new CancellationTokenSource();
        Task? third = null;
        try
        {
            await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            third = Task.Factory.StartNew(() => engine.InspectAsync(source, 8 * 1024 * 1024, cancelled.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            cancelled.CancelAfter(TimeSpan.FromMilliseconds(150));
            await Task.Delay(300);
            Assert.Equal(2, Volatile.Read(ref started));
        }
        finally
        {
            cancelled.Cancel(); release.Set();
            await Task.WhenAll(first, second);
            if (third is not null) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        }
        // Each completed inspection starts a version and a JSON process. The cancelled
        // waiter must consume neither, including after the first two release their slots.
        Assert.Equal(4, started);
    }
}
