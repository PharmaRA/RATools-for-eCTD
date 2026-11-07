using System.Reflection;
using System.Text.Json;
using Xunit;

internal static class SelectedTests
{
    private static async Task<int> Main(string[] args)
    {
#if UPLOAD
        Type[] suites = [typeof(RATools.Tests.PackageValidation.PackageUploadRetentionTests)];
        int[] expected = [11];
#elif CAPTURE
        Type[] suites = [typeof(RATools.Tests.Finalization.ReleaseCaptureServiceTests),
            typeof(RATools.Tests.Finalization.ReleaseCaptureBindingIntegrationTests),
            typeof(RATools.Tests.Finalization.RegionalMetadataCaptureTests),
            typeof(RATools.Tests.Finalization.FinalizationServiceTests),
            typeof(RATools.Tests.Finalization.ReleaseDeliveryPackageTests),
            typeof(RATools.Tests.Finalization.ReleaseCaptureTests)];
        int[] expected = [18, 2, 4, 9, 13, 6];
#else
        Type[] suites = [typeof(RATools.Tests.DocumentProcessing.QpdfConcurrencyTests),
            typeof(RATools.Tests.DocumentProcessing.QpdfProcessingEngineTests),
            typeof(RATools.Tests.DocumentProcessing.QpdfNavigationIntegrityTests),
            typeof(RATools.Tests.DocumentProcessing.PdfContentResourceTests),
            typeof(RATools.Tests.DocumentProcessing.PdfContentWorkflowTests)];
        int[] expected = [7, 19, 10, 5, 6];
#endif
        var results = new List<object>(); var counts = new List<object>();
        var passed = 0; var failed = 0; var skipped = 0;
        foreach (var (suite, suiteIndex) in suites.Select((suite, index) => (suite, index)))
        {
        var discovered = 0;
        foreach (var method in suite.GetMethods().Where(method => method.DeclaringType == suite && method.IsDefined(typeof(FactAttribute))))
        {
            var fact = method.GetCustomAttribute<FactAttribute>()!;
            if (fact.Skip is not null) { skipped++; results.Add(new { suite = suite.Name, method = method.Name, skipped = fact.Skip }); continue; }
            var data = method.GetCustomAttributes<InlineDataAttribute>().ToArray();
            if (method.IsDefined(typeof(TheoryAttribute)) && data.Length == 0)
                throw new InvalidOperationException("The bounded runner requires explicit InlineData cases.");
            var cases = data.Length == 0 ? new[] { Array.Empty<object?>() } : data.Select(value => value.GetData(method).Single()).ToArray();
            discovered += cases.Length;
            foreach (var (values, index) in cases.Select((values, index) => (values, index)))
            {
                try
                {
                    var instance = Activator.CreateInstance(suite)!;
                    try { if (method.Invoke(instance, values) is Task task) await task; }
                    finally
                    {
                        if (instance is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
                        else if (instance is IDisposable disposable) disposable.Dispose();
                    }
                    passed++; results.Add(new { suite = suite.Name, method = method.Name, index, passed = true });
                    Console.WriteLine($"PASS {suite.Name}.{method.Name}[{index}]");
                }
                catch (Exception error)
                {
                    failed++; var original = error is TargetInvocationException invocation ? invocation.InnerException! : error;
                    results.Add(new { suite = suite.Name, method = method.Name, index, passed = false, error = original.ToString() });
                    Console.WriteLine($"FAIL {suite.Name}.{method.Name}[{index}]: {original}");
                }
            }
        }
        counts.Add(new { suite = suite.Name, expected = expected[suiteIndex], discovered });
        if (discovered != expected[suiteIndex]) { failed++; Console.WriteLine($"FAIL {suite.Name} discovery: expected {expected[suiteIndex]}, actual {discovered}"); }
        }
        await File.WriteAllTextAsync(args[0], JsonSerializer.Serialize(new { passed, failed, skipped, suites = counts, cases = results }));
        Console.WriteLine($"TOTAL passed={passed} failed={failed} skipped={skipped}");
        return failed == 0 && skipped == 0 && passed == expected.Sum() ? 0 : 1;
    }
}
