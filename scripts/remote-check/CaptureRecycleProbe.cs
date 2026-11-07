using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

internal static class CaptureRecycleProbe
{
    private static async Task<int> Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        var passed = 0;
        try
        {
            var source = Path.Combine(root, "source", "0000");
            Directory.CreateDirectory(source);
            var bytes = "<synthetic-capture-evidence/>"u8.ToArray();
            await File.WriteAllBytesAsync(Path.Combine(source, "index.xml"), bytes);
            var captureRoot = Path.Combine(root, "captures");
            var reader = new PackageInputReader(captureRoot);
            var capture = await reader.ReadAsync(new(Guid.NewGuid(), "synthetic", source, "0000"), new());
            await capture.DisposeAsync();
            await capture.DisposeAsync();
            Require(!Directory.EnumerateDirectories(captureRoot, "input-*").Any(), "Active capture left after dispose");
            var recycled = Directory.GetDirectories(Path.Combine(captureRoot, "_recycle")).Single();
            Require(File.ReadAllBytes(Directory.GetFiles(recycled).Single()).SequenceEqual(bytes), "Recycled bytes changed");
            Require(File.ReadAllBytes(Path.Combine(source, "index.xml")).SequenceEqual(bytes), "Source changed");
            passed++; Console.WriteLine("PASS disposal preserves bytes and is idempotent");

            try { await reader.ReadAsync(new(Guid.NewGuid(), "bad-selection", source, "0001"), new()); throw new Exception("Wrong selection accepted"); }
            catch (PackageInputException) { }
            Require(Directory.GetDirectories(Path.Combine(captureRoot, "_recycle")).Length == 2, "Failed capture was not recycled");
            Require(!Directory.EnumerateDirectories(captureRoot, "input-*").Any(), "Failed active capture remains");
            passed++; Console.WriteLine("PASS failed capture preserves bytes and original error");

            var linkedRoot = Path.Combine(root, "linked-captures");
            var linkedReader = new PackageInputReader(linkedRoot);
            var linked = await linkedReader.ReadAsync(new(Guid.NewGuid(), "linked", source, "0000"), new());
            Directory.CreateSymbolicLink(Path.Combine(linkedRoot, "_recycle"), source);
            try { await linked.DisposeAsync(); throw new Exception("Linked recycle destination accepted"); }
            catch (PackageInputException) { }
            Require(Directory.EnumerateDirectories(linkedRoot, "input-*").Count() == 1, "Capture lost on refused cleanup");
            Require(Directory.GetFiles(source).Length == 1, "Source modified via recycle link");
            passed++; Console.WriteLine("PASS linked recycle destination refused with original capture retained");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed, failed = 0 }));
            return 0;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed, failed = 1, error = error.ToString() }));
            Console.Error.WriteLine(error); return 1;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
