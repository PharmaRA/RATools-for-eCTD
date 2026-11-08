using System.Text.Json;
using RATools.Tests.Applications;

if (!OperatingSystem.IsLinux() || new LinuxFactAttribute().Skip is not null)
    throw new InvalidOperationException("Real Linux execution is required.");

var tests = new ImportFileHashCacheTests();
var checks = new (string Name, Func<Task> Run)[]
{
    (nameof(tests.DistinctCaseSensitiveAddressesDoNotReuseTheFirstByteSourcesDigest), tests.DistinctCaseSensitiveAddressesDoNotReuseTheFirstByteSourcesDigest),
    (nameof(tests.LinuxFilesWhoseNamesDifferOnlyByCaseKeepSeparateMeasuredDigests), tests.LinuxFilesWhoseNamesDifferOnlyByCaseKeepSeparateMeasuredDigests),
    (nameof(tests.GetAsync_ComputesMd5AndSha256FromOneOpenPerPath), tests.GetAsync_ComputesMd5AndSha256FromOneOpenPerPath)
};
var outcomes = new List<object>();
var failed = 0;
foreach (var check in checks)
{
    try
    {
        await check.Run();
        outcomes.Add(new { name = check.Name, passed = true });
    }
    catch (Exception error)
    {
        failed++;
        outcomes.Add(new { name = check.Name, passed = false, error = error.ToString() });
    }
}
await File.WriteAllTextAsync(args[0], JsonSerializer.Serialize(new
{
    total = checks.Length, passed = checks.Length - failed, failed, skipped = 0, outcomes,
    scope = "Existing source test methods with real xUnit assertions on Linux; direct csc, not VSTest or full MSBuild"
}));
return failed == 0 ? 0 : 1;
