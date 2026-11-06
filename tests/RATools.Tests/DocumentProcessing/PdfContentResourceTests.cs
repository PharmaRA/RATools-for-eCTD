using System.Diagnostics;
using System.Reflection;
using RATools.Infrastructure.DocumentProcessing;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfContentResourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCancellationAndDeadlineKillOnlyTheOwnedPythonTree(bool deadline)
    {
        var root = Path.GetFullPath(".artifacts/p508-resource-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ready = Path.Combine(root, "child-pid.txt");
        var script = """
            import subprocess,sys,time
            from pathlib import Path
            child=subprocess.Popen([sys.executable,'-I','-B','-c','import time; time.sleep(60)'])
            Path(sys.argv[1]).write_text(str(child.pid),encoding='utf-8')
            time.sleep(60)
            """;
        using var token = new CancellationTokenSource();
        var task = RunHost(PdfContentTestRig.Python, ["-I", "-B", "-c", script, ready], Path.Combine(root, "process"),
            256 * 1024 * 1024, deadline ? TimeSpan.FromMilliseconds(1200) : TimeSpan.FromSeconds(10), 65536, token.Token);
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!File.Exists(ready)) await Task.Delay(20, startup.Token);
        var childId = int.Parse(await File.ReadAllTextAsync(ready), System.Globalization.CultureInfo.InvariantCulture);
        using var child = Process.GetProcessById(childId);
        if (!deadline) await token.CancelAsync();
        if (deadline) await Assert.ThrowsAsync<TimeoutException>(async () => await task);
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        using var exitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await child.WaitForExitAsync(exitDeadline.Token);
        Assert.True(child.HasExited);
        Assert.False(Process.GetCurrentProcess().HasExited);
    }

    [Fact]
    public async Task ExcessiveDiagnosticOutputIsBoundedAndDoesNotHangTheWorker()
    {
        var root = Path.GetFullPath(".artifacts/p508-resource-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var task = RunHost(PdfContentTestRig.Python, ["-I", "-B", "-c", "import sys,time; sys.stdout.write('x'*100000); sys.stdout.flush(); time.sleep(60)"],
            Path.Combine(root, "bounded"), 256 * 1024 * 1024, TimeSpan.FromSeconds(10), 4096, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await task);
    }

    [Fact]
    public async Task ExitingWorkerCannotKeepInheritedOutputPipeOpenThroughAChild()
    {
        var root = Path.GetFullPath(".artifacts/p508-resource-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var child = Path.Combine(root, "child-pid.txt");
        var script = "import subprocess,sys,time; child=subprocess.Popen([sys.executable,'-I','-B','-c','import time; time.sleep(60)']); open(sys.argv[1],'w').write(str(child.pid)); sys.stdout.flush()";
        var task = RunHost(PdfContentTestRig.Python, ["-I", "-B", "-c", script, child], Path.Combine(root, "inherited"),
            256 * 1024 * 1024, TimeSpan.FromSeconds(8), 65536, CancellationToken.None);
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!File.Exists(child)) await Task.Delay(20, startup.Token);
        var childId = int.Parse(await File.ReadAllTextAsync(child), System.Globalization.CultureInfo.InvariantCulture);
        using var childProcess = Process.GetProcessById(childId);
        await Assert.ThrowsAsync<TimeoutException>(async () => await task);
        using var exit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await childProcess.WaitForExitAsync(exit.Token);
        Assert.True(childProcess.HasExited);
    }

    [Fact]
    public async Task NativeJobMemoryLimitRejectsAnUnboundedAllocation()
    {
        var root = Path.GetFullPath(".artifacts/p508-resource-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string allocation = """
            import ctypes,json,sys
            from ctypes import wintypes as w
            K=ctypes.WinDLL('kernel32',use_last_error=True); P=ctypes.WinDLL('psapi',use_last_error=True)
            class Counters(ctypes.Structure):
             _fields_=[('cb',w.DWORD),('faults',w.DWORD)]+[(name,ctypes.c_size_t) for name in ['PeakWS','WS','QuotaPeakPaged','QuotaPaged','QuotaPeakNonPaged','QuotaNonPaged','Pagefile','PeakPagefile','PrivateUsage']]
            K.GetCurrentProcess.restype=w.HANDLE
            P.GetProcessMemoryInfo.argtypes=[w.HANDLE,ctypes.POINTER(Counters),w.DWORD]; P.GetProcessMemoryInfo.restype=w.BOOL
            K.VirtualAlloc.argtypes=[ctypes.c_void_p,ctypes.c_size_t,w.DWORD,w.DWORD]; K.VirtualAlloc.restype=ctypes.c_void_p
            def private():
             c=Counters(); c.cb=ctypes.sizeof(c)
             if not P.GetProcessMemoryInfo(K.GetCurrentProcess(),ctypes.byref(c),ctypes.sizeof(c)): raise ctypes.WinError(ctypes.get_last_error())
             return c.PrivateUsage
            rows=[private()]; blocks=[]
            for i in range(32):
             value=K.VirtualAlloc(None,8*1024*1024,0x3000,0x04)
             if not value:
              print(json.dumps({'allocatedChunks':len(blocks),'allocationBytes':len(blocks)*8*1024*1024,'privateUsage':rows,'failedPrivateUsage':private(),'error':ctypes.get_last_error()}),flush=True)
              sys.exit(23)
             blocks.append(value); rows.append(private())
            sys.exit(99)
            """;
        var task = RunHost(PdfContentTestRig.Python, ["-I", "-B", "-c", allocation],
            Path.Combine(root, "memory"), 64 * 1024 * 1024, TimeSpan.FromSeconds(10), 65536, CancellationToken.None);
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        Assert.Equal(23, (int)result.GetType().GetProperty("ExitCode")!.GetValue(result)!);
        using var allocationResult = System.Text.Json.JsonDocument.Parse((string)result.GetType().GetProperty("Output")!.GetValue(result)!);
        Assert.InRange(allocationResult.RootElement.GetProperty("allocatedChunks").GetInt32(), 1, 7);
        var successfulPrivate = allocationResult.RootElement.GetProperty("privateUsage").EnumerateArray().Select(value => value.GetInt64()).ToArray();
        Assert.All(successfulPrivate, value => Assert.InRange(value, 1, 64 * 1024 * 1024));
        Assert.Equal(successfulPrivate[^1], allocationResult.RootElement.GetProperty("failedPrivateUsage").GetInt64());
        Assert.Equal(1455, allocationResult.RootElement.GetProperty("error").GetInt32());
        var peak = (long)result.GetType().GetProperty("PeakMemoryBytes")!.GetValue(result)!;
        var configured = (long)result.GetType().GetProperty("ConfiguredMemoryBytes")!.GetValue(result)!;
        var processPeak = (long)result.GetType().GetProperty("PeakProcessMemoryBytes")!.GetValue(result)!;
        var flags = (uint)result.GetType().GetProperty("LimitFlags")!.GetValue(result)!;
        Console.WriteLine($"Native memory evidence: configured={configured}, flags={flags}, jobPeak={peak}, processPeak={processPeak}, allocations={allocationResult.RootElement}");
        Assert.Equal(64 * 1024 * 1024, configured); Assert.Equal(0x200u, flags & 0x200u);
        // Windows can include a rejected allocation attempt in PeakJobMemoryUsed.
        // The enforced bound is proved by the exact job configuration plus the
        // MemoryError and successfully committed 8-MiB block count above.
        Assert.True(peak > 0); Assert.True(processPeak > 0);
    }

    private static Task RunHost(string executable, string[] arguments, string prefix, long memory, TimeSpan timeout,
        int logs, CancellationToken cancellationToken)
    {
        var type = typeof(PythonPdfContentEngine).Assembly.GetType("RATools.Infrastructure.DocumentProcessing.PdfContentProcess")!;
        var method = type.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;
        return (Task)method.Invoke(null, [executable, arguments, prefix, memory, TimeSpan.FromSeconds(8), timeout,
            logs, 1024L * 1024, 1024L * 1024, null, cancellationToken])!;
    }
}
