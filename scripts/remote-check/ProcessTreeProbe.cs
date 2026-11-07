using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RATools.Infrastructure.DocumentProcessing;

internal static class ProcessTreeProbe
{
    private static readonly List<object> Cases = [];
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Actual Linux required");
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var failed = 0;
        foreach (var mode in new[] { "cancel", "timeout", "output-limit" })
        {
            try { await CheckAsync(root, mode); Cases.Add(new { mode, passed = true }); Console.WriteLine("PASS " + mode); }
            catch (Exception error) { failed++; Cases.Add(new { mode, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + mode + ": " + error); }
        }
        await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new {
            passed = Cases.Count - failed, failed, skipped = 0, cases = Cases,
            scope = "real QpdfProcessingEngine process host with controlled Python parent/child; not qpdf output or hard CPU/memory qualification"
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }

    private static async Task CheckAsync(string root, string mode)
    {
        var directory = Path.Combine(root, mode); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.pdf"); await File.WriteAllTextAsync(source, "synthetic tool-host input");
        var record = Path.Combine(directory, "pids.json");
        const string helper = """
            import json,os,subprocess,sys,time
            child=subprocess.Popen([sys.executable,'-I','-B','-c','import time; time.sleep(20)'])
            with open(sys.argv[1],'w') as f: json.dump([os.getpid(),child.pid],f)
            if sys.argv[2]=='output-limit':
                sys.stdout.write('x'*16384); sys.stdout.flush()
            time.sleep(20)
            """;
        using var budget = new PdfToolProcessBudget(1);
        using var cancel = new CancellationTokenSource();
        var options = new QpdfProcessingOptions {
            ExecutablePath = "/usr/bin/python3", ExecutableArguments = ["-I", "-B", "-c", helper, record, mode],
            ExecutionTimeout = TimeSpan.FromSeconds(mode == "timeout" ? 2 : 10)
        };
        var engine = new QpdfProcessingEngine(options, budget);
        var execution = engine.InspectAsync(source, 1024, cancel.Token);
        int[]? ids = null;
        try
        {
            var ready = Stopwatch.StartNew();
            while (ids is null && ready.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (File.Exists(record))
                {
                    try { ids = JsonSerializer.Deserialize<int[]>(await File.ReadAllTextAsync(record)); }
                    catch (JsonException) { }
                }
                if (ids is null) await Task.Delay(20);
            }
            Require(ids is { Length: 2 }, "Helper did not create parent/child evidence");
            if (mode == "cancel") await cancel.CancelAsync();
            Exception? outcome = null;
            try { await execution.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (Exception error) { outcome = error; }
            Require(mode switch {
                "cancel" => outcome is OperationCanceledException,
                "timeout" => outcome is TimeoutException && execution.IsCompleted,
                _ => outcome is InvalidOperationException
            }, "Unexpected process result: " + outcome);
            var stopped = Stopwatch.StartNew();
            while (ids!.Any(IsRunning) && stopped.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);
            Require(!ids!.Any(IsRunning), "Owned parent or child is still running");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var recovered = await budget.AcquireAsync(deadline.Token);
        }
        finally
        {
            // Always request production cleanup; helper itself is bounded to 20s.
            await cancel.CancelAsync();
            try { await execution.WaitAsync(TimeSpan.FromSeconds(12)); } catch (Exception) { }
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            var stat = File.ReadAllText("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat");
            var state = stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0];
            return state is not "Z" and not "X"; // Reaping is owned by the host init process.
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
