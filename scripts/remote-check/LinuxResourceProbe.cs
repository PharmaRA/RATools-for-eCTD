using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RATools.Infrastructure.DocumentProcessing;

internal static class LinuxResourceProbe
{
    private static async Task<int> Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var groupRoot = Environment.GetEnvironmentVariable("RATOOLS_PDF_CGROUP_ROOT") ?? throw new InvalidOperationException("Missing delegated cgroup");
        var results = new List<object>(); var failed = 0;
        foreach (var mode in new[] { "memory", "cpu", "accounting-fault" })
        {
            var before = Directory.GetDirectories(groupRoot).ToHashSet(StringComparer.Ordinal);
            try
            {
                var directory = Path.Combine(root, mode); Directory.CreateDirectory(directory);
                var source = Path.Combine(directory, "input.pdf"); await File.WriteAllTextAsync(source, "synthetic host input");
                var marker = Path.Combine(directory, "limit-escaped");
                const string helper = """
                    import pathlib,subprocess,sys,time
                    if '--version' in sys.argv:
                        print('qpdf version 12.4.1'); sys.exit(0)
                    mode,marker=sys.argv[1:3]
                    code="import time; x=bytearray(48*1024*1024); print('ready',flush=True); time.sleep(3)" if mode=='memory' else "import time; s=time.process_time();\nwhile time.process_time()-s<1: pass\nprint('ready',flush=True)"
                    children=[subprocess.Popen([sys.executable,'-I','-B','-c',code],stdout=subprocess.PIPE,text=True) for _ in range(2)]
                    if all(p.stdout.readline().strip()=='ready' for p in children): pathlib.Path(marker).write_text('resource limit was exceeded')
                    for p in children: p.wait()
                    print('{}')
                    """;
                using var budget = new PdfToolProcessBudget(1);
                var changed = new Dictionary<string, UnixFileMode>(StringComparer.Ordinal);
                var options = new QpdfProcessingOptions {
                    ExecutablePath = "/usr/bin/python3", ExecutableArguments = ["-I", "-B", "-c", helper, mode, marker],
                    MaxMemoryBytes = 64L * 1024 * 1024,
                    MaxCpuTime = mode == "cpu" ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(10),
                    ExecutionTimeout = TimeSpan.FromSeconds(10),
                    ProcessStartedCallback = _ => {
                        if (mode != "accounting-fault") return;
                        foreach (var group in Directory.GetDirectories(groupRoot).Where(path => !before.Contains(path)))
                        {
                            var stat = Path.Combine(group, "cpu.stat");
                            changed[stat] = File.GetUnixFileMode(stat);
                            File.SetUnixFileMode(stat, UnixFileMode.None);
                        }
                    }
                };
                if (mode == "accounting-fault") options.ExecutableArguments = ["-I", "-B", "-c", "import time; time.sleep(10)"];
                var engine = new QpdfProcessingEngine(options, budget);
                Exception? outcome = null;
                try { await engine.InspectAsync(source, 4096); }
                catch (Exception error) { outcome = error; }
                finally { foreach (var item in changed) File.SetUnixFileMode(item.Key, item.Value); }
                if (File.Exists(marker)) throw new InvalidOperationException("Configured " + mode + " limit did not stop the owned process tree");
                var groups = Directory.GetDirectories(groupRoot).Where(path => !before.Contains(path)).ToArray();
                if (groups.Length != (mode == "accounting-fault" ? 1 : 2)) throw new InvalidOperationException("Unexpected cgroup count " + groups.Length, outcome);
                var evidence = groups.Select(path => new {
                    path, memory = File.ReadAllText(Path.Combine(path, "memory.events")),
                    cpu = File.ReadAllText(Path.Combine(path, "cpu.stat")),
                    events = File.ReadAllText(Path.Combine(path, "cgroup.events"))
                }).ToArray();
                if (evidence.Any(item => !item.events.Contains("populated 0", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Task cgroup retains live processes");
                if (mode == "memory" && !evidence.Any(item => item.memory.Split('\n').Any(line => line.StartsWith("oom_kill ", StringComparison.Ordinal) && line != "oom_kill 0")))
                    throw new InvalidOperationException("No actual memory OOM enforcement evidence");
                if (mode == "cpu" && !evidence.Any(item => item.cpu.Split('\n').Any(line => line.StartsWith("usage_usec ", StringComparison.Ordinal) &&
                    long.Parse(line.AsSpan(11), System.Globalization.CultureInfo.InvariantCulture) >= 200000)))
                    throw new InvalidOperationException("No actual cumulative CPU budget evidence");
                if (mode == "accounting-fault" && outcome is not UnauthorizedAccessException)
                    throw new InvalidOperationException("Expected accounting read failure after full cleanup", outcome);
                await using var slot = await budget.AcquireAsync();
                results.Add(new { mode, passed = true, evidence }); Console.WriteLine("PASS " + mode);
            }
            catch (Exception error) { failed++; results.Add(new { mode, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + mode + ": " + error); }
        }
        await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed = results.Count - failed, failed, cases = results }, new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }
}
