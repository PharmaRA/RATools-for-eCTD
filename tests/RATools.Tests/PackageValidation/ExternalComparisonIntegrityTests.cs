using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.PackageValidation;

public sealed class ExternalComparisonIntegrityTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task IntactEvidenceReadsAndReplaysWithoutChangingItsIdentity()
    {
        await using var context = CreateContext();
        var (store, evidence) = await SeedAsync(context);
        var loaded = Assert.IsType<ExternalComparisonEvidence>(await store.GetExternalComparisonAsync(evidence.RunId));
        Assert.Equal(evidence.Digest, loaded.Digest);
        Assert.Equal(evidence.ReportSha256, loaded.ReportSha256);
        Assert.Equal(evidence.ReportText, await store.GetExternalComparisonReportTextAsync(evidence.RunId));
        Assert.False(await store.RegisterExternalComparisonAsync(evidence));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("differences")]
    [InlineData("package")]
    [InlineData("tool")]
    [InlineData("malformed-json")]
    public async Task AlteredPersistedEvidenceIsRejectedByBothReadsAndReplay(string mutation)
    {
        await using var context = CreateContext();
        var (store, evidence) = await SeedAsync(context);
        var row = await context.Set<PackageValidationExternalComparisonRecord>().SingleAsync();
        switch (mutation)
        {
            case "text": row.ReportText = "<altered />"; break;
            case "differences":
                row.DifferencesJson = JsonSerializer.Serialize(new[]
                {
                    evidence.Differences[0] with { Explanation = "Altered explanation." }
                }, SerializerOptions);
                break;
            case "package": row.PackageDigest = new string('c', 64); break;
            case "tool": row.ToolVersion = "2"; break;
            case "malformed-json": row.DifferencesJson = "{"; break;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetExternalComparisonAsync(evidence.RunId));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetExternalComparisonReportTextAsync(evidence.RunId));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RegisterExternalComparisonAsync(evidence));
    }

    [Fact]
    public async Task EvidenceCannotBeReinterpretedUnderAChangedRunBinding()
    {
        await using var context = CreateContext();
        var (store, evidence) = await SeedAsync(context);
        var run = await context.Set<PackageValidationRun>().SingleAsync();
        // A record's get-only property cannot be changed with `with`; replace the whole
        // binding through the run's private setter to simulate a rebound run row.
        typeof(PackageValidationRun).GetProperty(nameof(PackageValidationRun.Binding))!
            .SetValue(run, new ValidationBinding(run.Binding.InputManifestDigest, new string('c', 64),
                run.Binding.ProfileSnapshotId, run.Binding.RulesDigest, run.Binding.EngineVersion,
                run.Binding.ValidationMode, run.Binding.ResourceLimitsDigest));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetExternalComparisonAsync(evidence.RunId));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetExternalComparisonReportTextAsync(evidence.RunId));
    }

    private static RAToolsDbContext CreateContext() => new(new DbContextOptionsBuilder<RAToolsDbContext>()
        .UseInMemoryDatabase("external-integrity-" + Guid.NewGuid().ToString("N")).Options);

    private static async Task<(EfCorePackageValidationStore Store, ExternalComparisonReport Evidence)> SeedAsync(RAToolsDbContext context)
    {
        var binding = new ValidationBinding(new string('a', 64), new string('b', 64), PackageValidationCatalog.IchProfile,
            PackageValidationCatalog.Current.Digest, "test-delivery-v1", PackageValidationMode.Development,
            new PackageReadLimits().Digest());
        var run = new PackageValidationRun(Guid.NewGuid(), "0000", binding, "{}", Guid.NewGuid().ToString("N"), Now);
        var token = run.Claim("test", Now, TimeSpan.FromMinutes(1));
        var report = ValidationReport.Create(PackageValidationCatalog.Current, binding, ValidationRunStatus.Completed,
            [new RuleCheckResult("FILE-HREF", CheckStatus.Pass, "No missing payloads.")]);
        run.Complete(token, "test", Now.AddSeconds(1), report, new string('d', 64));
        context.Add(run);
        await context.SaveChangesAsync();
        var evidence = new ExternalComparisonReport(run.Id, binding, "validator", "1", binding.InputManifestDigest,
            "<report />", [new("EXT-1", "FILE-HREF", "A difference.", "Reviewed for this exact package.")], Now);
        var store = new EfCorePackageValidationStore(context);
        Assert.True(await store.RegisterExternalComparisonAsync(evidence));
        return (store, evidence);
    }
}
