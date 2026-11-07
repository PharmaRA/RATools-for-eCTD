using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;

namespace RATools.Infrastructure.DocumentProcessing;

// Requires an explicitly delegated cgroup-v2 subtree owned by the service account.
// Completed empty groups are retained; this class never removes filesystem objects.
internal sealed class LinuxPdfResourceScope : IDisposable
{
    private readonly string _directory;
    private readonly CancellationTokenSource _stop = new();
    private Task _monitor = Task.CompletedTask;
    private readonly TimeSpan _cpuLimit;
    private bool _closed;

    private LinuxPdfResourceScope(string directory, TimeSpan cpuLimit)
    {
        _directory = directory;
        _cpuLimit = cpuLimit;
    }

    public static LinuxPdfResourceScope Create(long memoryBytes, TimeSpan cpuLimit)
    {
        var configured = Environment.GetEnvironmentVariable("RATOOLS_PDF_CGROUP_ROOT");
        if (string.IsNullOrWhiteSpace(configured))
            throw new PlatformNotSupportedException("Linux PDF tools require RATOOLS_PDF_CGROUP_ROOT naming a delegated cgroup-v2 subtree.");
        var root = Path.GetFullPath(configured);
        if (!root.StartsWith("/sys/fs/cgroup/", StringComparison.Ordinal))
            throw new InvalidOperationException("The Linux PDF cgroup root must be below /sys/fs/cgroup.");
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The Linux PDF cgroup path must exist without links.");
        var controllers = File.ReadAllText(Path.Combine(root, "cgroup.subtree_control")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (new[] { "cpu", "memory", "pids" }.Any(controller => !controllers.Contains(controller, StringComparer.Ordinal)))
            throw new InvalidOperationException("The delegated Linux PDF subtree must enable cpu, memory and pids controllers.");
        var directory = Path.Combine(root, "pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // Configure every limit before the shell can enter and exec the tool.
        File.WriteAllText(Path.Combine(directory, "memory.max"), memoryBytes.ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(directory, "memory.swap.max"), "0");
        File.WriteAllText(Path.Combine(directory, "memory.oom.group"), "1");
        File.WriteAllText(Path.Combine(directory, "pids.max"), "4");
        File.WriteAllText(Path.Combine(directory, "cpu.max"), "100000 100000");
        if (!File.Exists(Path.Combine(directory, "cgroup.kill")))
            throw new PlatformNotSupportedException("Linux PDF enforcement requires cgroup.kill support.");
        return new(directory, cpuLimit);
    }

    public ProcessStartInfo StartInfo(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo("/bin/sh") {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("printf '%s' \"$$\" > \"$1/cgroup.procs\" || exit 125; IFS= read -r gate || exit 125; [ \"$gate\" = start ] || exit 125; shift; exec \"$@\"");
        info.ArgumentList.Add("ratools-pdf");
        info.ArgumentList.Add(_directory);
        info.ArgumentList.Add(executable);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public void Release(Process process)
    {
        var pid = process.Id.ToString(CultureInfo.InvariantCulture);
        var deadline = Stopwatch.StartNew();
        while (!File.ReadAllLines(Path.Combine(_directory, "cgroup.procs")).Contains(pid, StringComparer.Ordinal))
        {
            if (process.HasExited || deadline.Elapsed > TimeSpan.FromSeconds(5))
                throw new InvalidOperationException("The PDF launcher could not enter its delegated Linux resource group.");
            Thread.Sleep(5);
        }
        // Enrollment is confirmed before accounting starts and tool code is released.
        _monitor = MonitorAsync(_cpuLimit);
        process.StandardInput.WriteLine("start");
        process.StandardInput.Close();
    }

    private async Task MonitorAsync(TimeSpan cpuLimit)
    {
        try
        {
            while (true)
            {
                await Task.Delay(20, _stop.Token).ConfigureAwait(false);
                var usage = File.ReadLines(Path.Combine(_directory, "cpu.stat"))
                    .Single(line => line.StartsWith("usage_usec ", StringComparison.Ordinal));
                if (long.Parse(usage.AsSpan("usage_usec ".Length), CultureInfo.InvariantCulture) >= cpuLimit.TotalMilliseconds * 1000)
                {
                    Kill();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch
        {
            // Loss of accounting must stop the tool instead of disabling its budget.
            Kill();
            throw;
        }
    }

    private void Kill() => File.WriteAllText(Path.Combine(_directory, "cgroup.kill"), "1");

    public void Dispose()
    {
        if (_closed) return;
        Exception? failure = null;
        try
        {
            _stop.Cancel();
            Kill();
            try { _monitor.GetAwaiter().GetResult(); }
            catch (Exception error) { failure = error; }
            var deadline = Stopwatch.StartNew();
            while (File.ReadAllText(Path.Combine(_directory, "cgroup.events")).Contains("populated 1", StringComparison.Ordinal))
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(5))
                    throw new InvalidOperationException("The Linux PDF resource group did not stop.");
                Thread.Sleep(10);
            }
            _closed = true;
        }
        finally { if (_closed) _stop.Dispose(); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
