using System.Text.Json;
using Microsoft.Extensions.Options;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.PackageValidation;

public sealed class PackageInputPreflightServiceTests
{
    [Fact]
    public async Task PendingBytesAreNotAddressableAsCompletedEvidence()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var directory = Path.Combine(root, "validation/preflight"); Directory.CreateDirectory(directory);
            var id = new string('a', 64);
            File.WriteAllText(Path.Combine(directory, id + "." + Guid.NewGuid().ToString("N") + ".pending"), "partial");
            Assert.Null(await Create(root).GetAsync(id));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public async Task ConcurrentIdenticalAssessmentsPublishOneCompleteResult()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider());
            var snapshot = await snapshots.CaptureAsync("us-fda-ectd-3.2.2");
            var clock = new FixedClock();
            var service = new PackageInputPreflightService(new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [root] })),
                Options.Create(new PackageValidationOptions { StorageRoot = Path.Combine(root, "validation") }), snapshots, clock);
            var request = new PackageInputPreflightRequest(Guid.NewGuid(), PackageInputPreflightTests.CreateInput(root, "empty-directory"),
                PackageInputRole.Directory, PackageValidationCatalog.UsProfile, snapshot.Id);
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.InspectAsync(request)));
            Assert.Single(results.Select(item => item.Id).Distinct(StringComparer.Ordinal));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "validation/preflight"), "*.json"));
            var reread = await service.GetAsync(results[0].Id);
            Assert.NotNull(reread);
            Assert.Equal(results[0].Assessment.GetRawText(), reread.Assessment.GetRawText());
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Fact]
    public async Task MissingEvidenceGetDoesNotCreateStorage()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            Assert.Null(await Create(root).GetAsync(new string('a', 64)));
            Assert.False(Directory.Exists(Path.Combine(root, "validation")));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public void DiskRootOverlapIsRecognizedWithoutReadingTheDisk()
    {
        var root = Path.GetPathRoot(Path.GetFullPath("."))!;
        var within = typeof(PackageInputReader).GetMethod("IsWithin", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.True((bool)within.Invoke(null, [Path.Combine(root, "synthetic-preflight-storage"), root])!);
    }

    [Fact]
    public async Task EmptyInputProducesFrozenBoundEvidenceThatCanBeReadByANewService()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var source = PackageInputPreflightTests.CreateInput(root, "empty-directory");
            var service = Create(root);
            var first = await service.InspectAsync(new(Guid.NewGuid(), source, PackageInputRole.Directory, PackageValidationCatalog.UsProfile));
            Assert.Equal(64, first.Id.Length);
            Assert.False(first.Assessment.GetProperty("isReadyForFinalization").GetBoolean());
            Assert.Equal("InputShapeOnly", first.Assessment.GetProperty("scope").GetString());
            Assert.NotNull(first.Assessment.GetProperty("standardsSnapshotId").GetString());
            var checks = first.Assessment.GetProperty("checks").EnumerateArray();
            Assert.Equal("Fail", checks.Single(item => item.GetProperty("ruleId").GetString() == "FDA47-4").GetProperty("status").GetString());
            Assert.DoesNotContain(StringValues(first.Assessment), value => value.Replace('\\', '/').Contains(root.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            var stored = File.ReadAllBytes(Path.Combine(root, "validation/preflight", first.Id + ".json"));
            Assert.Equal(first.Id, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stored)).ToLowerInvariant());
            Assert.Equal(first.Assessment.GetRawText(), System.Text.Encoding.UTF8.GetString(stored));
            File.WriteAllText(Path.Combine(source, "new.txt"), "changed");
            var reread = await Create(root).GetAsync(first.Id);
            Assert.NotNull(reread);
            Assert.Equal(first.Assessment.GetRawText(), reread.Assessment.GetRawText());
            var next = await service.InspectAsync(new(first.Assessment.GetProperty("applicationId").GetGuid(), source, PackageInputRole.Directory, PackageValidationCatalog.UsProfile));
            Assert.NotEqual(first.Id, next.Id);
            Assert.NotEqual(first.Assessment.GetProperty("inputDigest").GetString(), next.Assessment.GetProperty("inputDigest").GetString());
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public async Task ExplicitSnapshotIsFrozenAndInvalidSelectionsNeverFallback()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider());
            var snapshot = await snapshots.CaptureAsync("us-fda-ectd-3.2.2");
            var service = Create(root, snapshots);
            var request = new PackageInputPreflightRequest(Guid.NewGuid(), PackageInputPreflightTests.CreateInput(root, "empty-directory"),
                PackageInputRole.Directory, PackageValidationCatalog.UsProfile, snapshot.Id);
            var receipt = await service.InspectAsync(request);
            Assert.Equal(snapshot.Id, receipt.Assessment.GetProperty("standardsSnapshotId").GetString());
            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), receipt.Assessment.GetProperty("selectionDate").GetString());
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.InspectAsync(request with { StandardsSnapshotId = new string('a', 64) }));
            await Assert.ThrowsAsync<StandardsSelectionException>(() => service.InspectAsync(request with { ProfileSnapshotId = PackageValidationCatalog.IchProfile }));
            await Assert.ThrowsAsync<StandardsSelectionException>(() => service.InspectAsync(request with { SelectionDate = new DateOnly(2099, 1, 1) }));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public void ParsedStringCheckSeesEscapedWindowsPaths()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { path = @"C:\work\source" }));
        Assert.Contains(StringValues(document.RootElement), value => value.Replace('\\', '/').Contains("C:/work/source", StringComparison.Ordinal));
    }

    private static IEnumerable<string> StringValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) yield return element.GetString()!;
        else if (element.ValueKind == JsonValueKind.Object)
        { foreach (var property in element.EnumerateObject()) foreach (var value in StringValues(property.Value)) yield return value; }
        else if (element.ValueKind == JsonValueKind.Array)
        { foreach (var child in element.EnumerateArray()) foreach (var value in StringValues(child)) yield return value; }
    }

    [Fact]
    public async Task RefusesInvalidEvidenceIdAndTamperedArchivedBytes()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var service = Create(root);
            await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync("../source"));
            Assert.Null(await service.GetAsync(new string('a', 64)));
            var receipt = await service.InspectAsync(new(Guid.NewGuid(), PackageInputPreflightTests.CreateInput(root, "single"), PackageInputRole.SingleFile, PackageValidationCatalog.UsProfile));
            File.WriteAllText(Path.Combine(root, "validation/preflight", receipt.Id + ".json"), "{}");
            await Assert.ThrowsAsync<InvalidDataException>(() => service.GetAsync(receipt.Id));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    [Fact]
    public async Task RefusesPathOutsideAllowedRootAndHistoricalDateForNewAssessment()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var service = Create(root);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.InspectAsync(new(Guid.NewGuid(), Path.GetPathRoot(root)!, PackageInputRole.Directory, PackageValidationCatalog.UsProfile)));
            await Assert.ThrowsAsync<StandardsSelectionException>(() => service.InspectAsync(new(Guid.NewGuid(), root, PackageInputRole.Directory,
                PackageValidationCatalog.UsProfile, SelectionDate: new DateOnly(2000, 1, 1))));
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }

    internal static PackageInputPreflightService Create(string root, StandardsSnapshotService? snapshots = null) => new(
        new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [root] })),
        Options.Create(new PackageValidationOptions { StorageRoot = Path.Combine(root, "validation") }),
        snapshots ?? new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()));
}
