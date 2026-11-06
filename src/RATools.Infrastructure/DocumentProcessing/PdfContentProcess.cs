using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RATools.Infrastructure.DocumentProcessing;

internal sealed record ContentProcessResult(int ExitCode, string Output, long PeakMemoryBytes, long CpuMilliseconds, long WallMilliseconds,
    long ConfiguredMemoryBytes, long PeakProcessMemoryBytes, uint LimitFlags);

internal static class PdfContentProcess
{
    public static async Task<ContentProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string artifactPrefix,
        long memoryBytes, TimeSpan cpuLimit, TimeSpan timeout, int logLimit, long outputLimit, long reportLimit, string? outputPath,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Content process resource enforcement is qualified on Windows only.");
        using var deadline = new CancellationTokenSource(timeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var started = Stopwatch.GetTimestamp();
        using var job = new ProcessJob(memoryBytes, cpuLimit);
        using var launch = SuspendedProcess.Start(executable, arguments, Path.GetDirectoryName(artifactPrefix)!);
        var process = launch.Process;
        try { job.Assign(process); launch.Resume(); }
        catch { Kill(process); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); throw; }
        using var monitors = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var output = ReadBoundedAsync(launch.Stdout, logLimit, stop.Token);
        var errors = ReadBoundedAsync(launch.Stderr, logLimit, stop.Token);
        var wait = process.WaitForExitAsync(stop.Token);
        var monitor = MonitorOutputAsync(outputPath, artifactPrefix + ".receipt.json", outputLimit, reportLimit, monitors.Token);
        try
        {
            var pending = new List<Task> { wait, output, errors, monitor };
            while (!wait.IsCompleted)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                pending.Remove(completed);
            }
            await wait.ConfigureAwait(false);
            var stdout = await output.ConfigureAwait(false); var stderr = await errors.ConfigureAwait(false);
            await WriteNewAsync(artifactPrefix + ".stdout.txt", stdout, cancellationToken).ConfigureAwait(false);
            await WriteNewAsync(artifactPrefix + ".stderr.txt", stderr, cancellationToken).ConfigureAwait(false);
            var usage = job.ReadUsage();
            return new(process.ExitCode, stdout, usage.Memory, usage.Cpu, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                usage.Limit, usage.ProcessMemory, usage.Flags);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            Kill(process); throw new TimeoutException("The PDF content process exceeded its host deadline.");
        }
        catch { Kill(process); throw; }
        finally
        {
            await monitors.CancelAsync().ConfigureAwait(false);
            if (!process.HasExited) Kill(process);
            job.Dispose();
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await ObserveAsync(output).ConfigureAwait(false); await ObserveAsync(errors).ConfigureAwait(false);
            await ObserveAsync(monitor).ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream(); var buffer = new byte[4096]; int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > limit) throw new InvalidDataException("The PDF process diagnostic stream exceeded its limit.");
            bytes.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    private static async Task MonitorOutputAsync(string? path, string reportPath, long limit, long reportLimit, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            if (path is not null && File.Exists(path) && new FileInfo(path).Length > limit)
                throw new InvalidDataException("The PDF process output exceeded its host limit.");
            if (File.Exists(reportPath) && new FileInfo(reportPath).Length > reportLimit)
                throw new InvalidDataException("The PDF process receipt exceeded its host limit.");
        }
    }
    public static async Task WriteNewAsync(string path, string text, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);
        await file.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
    }
    private static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or InvalidDataException) { }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    // The process cannot execute user code or create children before it belongs
    // to the resource-limited job. Process.Start followed by assignment has a
    // measurable allocation and child-process escape window.
    internal sealed class SuspendedProcess : IDisposable
    {
        private readonly SafeFileHandle _thread;
        private SuspendedProcess(Process process, SafeFileHandle thread, FileStream stdout, FileStream stderr)
        { Process = process; _thread = thread; Stdout = stdout; Stderr = stderr; }
        public Process Process { get; }
        public FileStream Stdout { get; }
        public FileStream Stderr { get; }
        public static SuspendedProcess Start(string executable, IReadOnlyList<string> arguments, string directory)
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = 1 };
            if (!CreatePipe(out var stdoutReadRaw, out var stdoutWriteRaw, ref security, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var stdoutRead = new SafeFileHandle(stdoutReadRaw, true);
            using var stdoutWrite = new SafeFileHandle(stdoutWriteRaw, true);
            SafeFileHandle? stderrRead = null; Process? process = null; SafeFileHandle? thread = null;
            FileStream? stdout = null; FileStream? stderr = null;
            try
            {
                if (!CreatePipe(out var stderrReadRaw, out var stderrWriteRaw, ref security, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                stderrRead = new(stderrReadRaw, true);
                using var stderrWrite = new SafeFileHandle(stderrWriteRaw, true);
                if (!SetHandleInformation(stdoutRead, 1, 0) || !SetHandleInformation(stderrRead, 1, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                using var input = CreateFile("NUL", 0x80000000, 3, ref security, 3, 0, IntPtr.Zero);
                if (input.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                var startup = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x100,
                    StandardInput = input.DangerousGetHandle(), StandardOutput = stdoutWrite.DangerousGetHandle(),
                    StandardError = stderrWrite.DangerousGetHandle()
                };
                var command = new StringBuilder(Quote(executable) + string.Concat(arguments.Select(argument => " " + Quote(argument))));
                if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08000404, IntPtr.Zero,
                    directory, ref startup, out var info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                using var processHandle = new SafeFileHandle(info.Process, true);
                thread = new(info.Thread, true);
                process = Process.GetProcessById(checked((int)info.ProcessId));
                // Anonymous CreatePipe handles are synchronous. FileStream performs
                // async reads on these handles without pretending they are overlapped.
                stdout = new FileStream(stdoutRead, FileAccess.Read, 4096, isAsync: false);
                stderr = new FileStream(stderrRead, FileAccess.Read, 4096, isAsync: false);
                return new(process, thread, stdout, stderr);
            }
            catch
            {
                if (process is not null) { Kill(process); process.Dispose(); }
                stdout?.Dispose(); stderr?.Dispose(); stdoutRead.Dispose(); stderrRead?.Dispose(); thread?.Dispose();
                throw;
            }
        }
        public void Resume()
        {
            if (ResumeThread(_thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Dispose() { Stdout.Dispose(); Stderr.Dispose(); _thread.Dispose(); Process.Dispose(); }
        private static string Quote(string value)
        {
            var result = new StringBuilder(value.Length + 2).Append('"'); var slashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size; public string? Reserved; public string? Desktop; public string? Title; public int X; public int Y;
            public int XSize; public int YSize; public int XCount; public int YCount; public int Fill; public int Flags;
            public short Show; public short Reserved2; public IntPtr Reserved2Ptr; public IntPtr StandardInput; public IntPtr StandardOutput; public IntPtr StandardError;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process; public IntPtr Thread; public uint ProcessId; public uint ThreadId; }
        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }
        #pragma warning disable CA1838
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref SecurityAttributes attributes, int size);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint ResumeThread(SafeFileHandle thread);
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, ref SecurityAttributes security, uint disposition, uint flags, IntPtr template);
        #pragma warning restore CA1838
    }

    internal sealed class ProcessJob : IDisposable
    {
        private readonly SafeFileHandle _handle;
        public ProcessJob(long memoryBytes, TimeSpan cpuLimit)
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimits
            {
                Basic = new BasicLimits { Flags = 0x2000 | 0x200 | 0x4 | 0x8, JobUserTimeLimit = cpuLimit.Ticks, ActiveProcessLimit = 4 },
                JobMemoryLimit = checked((UIntPtr)(ulong)memoryBytes)
            };
            if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
            { var error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error); }
        }
        public void Assign(Process process)
        {
            if (!AssignProcessToJobObject(_handle, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public (long Memory, long Cpu, long Limit, long ProcessMemory, uint Flags) ReadUsage()
        {
            if (!QueryLimits(_handle, 9, out var limits, (uint)Marshal.SizeOf<ExtendedLimits>(), IntPtr.Zero) ||
                !QueryAccounting(_handle, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return ((long)limits.PeakJobMemoryUsed.ToUInt64(), (accounting.TotalUserTime + accounting.TotalKernelTime) / TimeSpan.TicksPerMillisecond,
                (long)limits.JobMemoryLimit.ToUInt64(), (long)limits.PeakProcessMemoryUsed.ToUInt64(), limits.Basic.Flags);
        }
        public void Dispose() => _handle.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessUserTimeLimit; public long JobUserTimeLimit; public uint Flags;
        public UIntPtr MinimumWorkingSet; public UIntPtr MaximumWorkingSet; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations; public ulong WriteOperations; public ulong OtherOperations;
        public ulong ReadBytes; public ulong WriteBytes; public ulong OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long TotalUserTime; public long TotalKernelTime; public long PeriodUserTime; public long PeriodKernelTime;
        public uint TotalPageFaultCount; public uint TotalProcesses; public uint ActiveProcesses; public uint TerminatedProcesses;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int info, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryLimits(SafeFileHandle job, int info, out ExtendedLimits limits, uint length, IntPtr returned);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryAccounting(SafeFileHandle job, int info, out Accounting accounting, uint length, IntPtr returned);
}
