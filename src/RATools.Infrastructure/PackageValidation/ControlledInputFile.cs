using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RATools.Application.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

internal static class ControlledInputFile
{
    public static FileStream OpenRead(string path)
    {
        PackageInputReader.EnsureNoLinks(path);
        FileStream stream;
        if (OperatingSystem.IsWindows())
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                var actual = new char[32_768];
                var count = NativeMethods.GetFinalPathNameByHandle(stream.SafeFileHandle, actual, (uint)actual.Length, 0);
                if (count == 0 || count >= actual.Length || NativeMethods.GetFileType(stream.SafeFileHandle) != 1)
                    throw new IOException("Cannot establish the input file's physical disk identity.");
                var resolved = new string(actual, 0, (int)count);
                if (resolved.StartsWith("\\\\?\\UNC\\", StringComparison.Ordinal)) resolved = "\\\\" + resolved[8..];
                else if (resolved.StartsWith("\\\\?\\", StringComparison.Ordinal)) resolved = resolved[4..];
                if (!string.Equals(Path.GetFullPath(path), resolved, StringComparison.OrdinalIgnoreCase)) throw UnsafePath();
            }
            catch { stream.Dispose(); throw; }
        }
        else if (OperatingSystem.IsLinux())
        {
            // O_NONBLOCK prevents FIFO/device opens from blocking before the
            // cancellation token can be observed; O_NOFOLLOW refuses leaf links.
            var descriptor = NativeMethods.Open(Encoding.UTF8.GetBytes(path + '\0'), 2048 | 131072 | 524288);
            if (descriptor < 0) throw new IOException("Cannot open controlled input.", new Win32Exception(Marshal.GetLastPInvokeError()));
            var handle = new SafeFileHandle(descriptor, ownsHandle: true);
            try
            {
                var resolved = File.ResolveLinkTarget($"/proc/self/fd/{descriptor}", returnFinalTarget: true)?.FullName;
                if (!string.Equals(Path.GetFullPath(path), resolved, StringComparison.Ordinal)) throw UnsafePath();
                stream = new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
            }
            catch { handle.Dispose(); throw; }
        }
        else throw new PlatformNotSupportedException("Controlled package capture is currently supported on Windows and Linux.");
        try
        {
            if (!stream.CanSeek) throw new PackageInputException("INPUT-PATHS", "UNSAFE_INPUT_PATH", "Only seekable regular input files are supported.");
            PackageInputReader.EnsureNoLinks(path);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private static PackageInputException UnsafePath() => new("INPUT-PATHS", "UNSAFE_INPUT_PATH", "The opened file identity differs from its validated physical path.");

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, [Out] char[] path, uint length, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint GetFileType(SafeFileHandle handle);

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Open(byte[] path, int flags);
    }
}
