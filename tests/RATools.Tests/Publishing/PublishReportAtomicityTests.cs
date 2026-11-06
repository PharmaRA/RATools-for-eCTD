using RATools.Application.Abstractions.Security;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

public sealed class PublishReportAtomicityTests
{
    [Fact]
    public async Task CancellationDuringReportWritePreservesThePreviousCompleteReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "ratools-qa02-retained", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var reportPath = Path.Combine(root, "report.json");
        const string previous = "{\"complete\":\"previous\"}";
        await File.WriteAllTextAsync(reportPath, previous);
        var replacement = new string('x', 32 * 1024 * 1024);
        var store = new LocalPublishArtifactStore(new ExactRootPolicy(root));
        using var cancellation = new CancellationTokenSource();
        var write = store.WriteAllTextAsync(reportPath, replacement, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);

        Assert.Equal(previous, await File.ReadAllTextAsync(reportPath));
        Assert.Empty(Directory.EnumerateFiles(root, "*.pending-*"));
    }

    [Fact]
    public async Task SuccessfulReplacementPublishesTheCompleteTextAndRecyclesPreviousBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ratools-qa02-retained", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var reportPath = Path.Combine(root, "report.json");
        await File.WriteAllTextAsync(reportPath, "previous");
        var store = new LocalPublishArtifactStore(new ExactRootPolicy(root));

        await store.WriteAllTextAsync(reportPath, "{\"complete\":true}");

        Assert.Equal("{\"complete\":true}", await File.ReadAllTextAsync(reportPath));
        Assert.Empty(Directory.EnumerateFiles(root, "*.pending-*"));
        Assert.Empty(Directory.EnumerateFiles(root, "*.previous-*"));
    }

    private sealed class ExactRootPolicy(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];

        public string EnsureAllowed(string path)
        {
            var absolute = Path.GetFullPath(path);
            if (!absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Outside the task-owned root.");
            return absolute;
        }
    }
}
