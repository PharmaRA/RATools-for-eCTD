using System.Diagnostics;

namespace RATools.Infrastructure.DocumentProcessing;

// Reuses the content engine's suspended launch: assignment precedes Resume, so
// tool code and descendants cannot escape the Windows job before limits apply.
internal sealed class PdfToolProcess : IDisposable
{
    private readonly PdfContentProcess.ProcessJob? _job;
    private readonly PdfContentProcess.SuspendedProcess? _launch;
    private PdfToolProcess(Process process, PdfContentProcess.ProcessJob? job = null,
        PdfContentProcess.SuspendedProcess? launch = null)
    { Process = process; _job = job; _launch = launch; }

    public Process Process { get; }
    public Stream Stdout => _launch?.Stdout ?? Process.StandardOutput.BaseStream;
    public Stream Stderr => _launch?.Stderr ?? Process.StandardError.BaseStream;

    public static PdfToolProcess Start(QpdfProcessingOptions options, IReadOnlyList<string> arguments)
    {
        var allArguments = options.ExecutableArguments.Concat(arguments).ToArray();
        if (OperatingSystem.IsWindows())
        {
            var job = new PdfContentProcess.ProcessJob(options.MaxMemoryBytes, options.MaxCpuTime);
            PdfContentProcess.SuspendedProcess? launch = null;
            try
            {
                launch = PdfContentProcess.SuspendedProcess.Start(ResolveExecutable(options.ExecutablePath), allArguments,
                    Environment.CurrentDirectory);
                job.Assign(launch.Process);
                launch.Resume();
                return new(launch.Process, job, launch);
            }
            catch
            {
                job.Dispose();
                if (launch is not null)
                {
                    try { if (!launch.Process.HasExited) launch.Process.Kill(entireProcessTree: true); }
                    finally { launch.Process.WaitForExit(); launch.Dispose(); }
                }
                throw;
            }
        }
        var process = new Process { StartInfo = new ProcessStartInfo(options.ExecutablePath)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var argument in allArguments) process.StartInfo.ArgumentList.Add(argument);
        try { process.Start(); return new(process); }
        catch { process.Dispose(); throw; }
    }

    private static string ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
            return Path.GetFullPath(executable);
        var file = Path.HasExtension(executable) ? executable : executable + ".exe";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var candidate = Path.Combine(directory.Trim('"'), file);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        return executable;
    }

    public void CloseJob() => _job?.Dispose();

    public void Dispose()
    {
        _job?.Dispose();
        if (_launch is not null) _launch.Dispose();
        else Process.Dispose();
    }
}
