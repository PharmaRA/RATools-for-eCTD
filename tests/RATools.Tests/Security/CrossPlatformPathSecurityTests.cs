using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Security;

[Trait("Category", "PathSecurity")]
public sealed class CrossPlatformPathSecurityTests
{
    private static readonly Guid ApplicationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void WorkspacePolicy_UsesPlatformFileSystemCaseSensitivity()
    {
        using var root = new TemporaryDirectory("ratools-path-case");
        var policy = CreatePolicy(root.Path);
        var caseVariant = ToggleAsciiCase(root.Path);
        Assert.NotEqual(root.Path, caseVariant);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.GetFullPath(caseVariant), policy.EnsureAllowed(caseVariant));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => policy.EnsureAllowed(caseVariant));
        }
    }

    [Fact]
    public async Task WorkspacePolicy_RejectsPlatformDirectoryLinkWithoutChangingExternalTarget()
    {
        using var allowedRoot = new TemporaryDirectory("ratools-path-allowed");
        using var outsideRoot = new TemporaryDirectory("ratools-path-outside");
        var sentinelPath = Path.Combine(outsideRoot.Path, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "must remain unchanged");
        var linkPath = Path.Combine(allowedRoot.Path, "linked-workspace");
        CreatePlatformDirectoryLink(linkPath, outsideRoot.Path);

        try
        {
            var before = CaptureFileSystemState(outsideRoot.Path);
            var policy = CreatePolicy(allowedRoot.Path);

            var exception = Assert.Throws<InvalidOperationException>(() => policy.EnsureAllowed(
                Path.Combine(linkPath, "sentinel.txt")));

            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, CaptureFileSystemState(outsideRoot.Path));
        }
        finally
        {
            DeleteDirectoryLink(linkPath);
        }
    }

    [Theory]
    [InlineData("_jobs")]
    [InlineData("_artifacts")]
    [InlineData("_packages")]
    public async Task PublishWriter_RejectsPlatformDirectoryLinkBeforeChangingExternalTarget(
        string linkedApplicationChild)
    {
        using var outputRoot = new TemporaryDirectory("ratools-publish-output");
        using var outsideRoot = new TemporaryDirectory("ratools-publish-outside");
        var sentinelPath = Path.Combine(outsideRoot.Path, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "must remain unchanged");
        var applicationRoot = Path.Combine(outputRoot.Path, ApplicationId.ToString("N"));
        Directory.CreateDirectory(applicationRoot);
        var linkPath = Path.Combine(applicationRoot, linkedApplicationChild);
        CreatePlatformDirectoryLink(linkPath, outsideRoot.Path);

        try
        {
            var before = CaptureFileSystemState(outsideRoot.Path);
            var writer = CreateWriter(outputRoot.Path);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveAsync(
                ApplicationId,
                "0001",
                Guid.NewGuid(),
                [new BackboneGeneratedFile("index.xml", "<ectd:ectd />")],
                "publish-report.json",
                "0001.zip",
                []));

            Assert.Contains("reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, CaptureFileSystemState(outsideRoot.Path));
        }
        finally
        {
            DeleteDirectoryLink(linkPath);
        }
    }

    [Fact]
    public async Task PublishRetention_SkipsPlatformDirectoryLinkWithoutChangingExternalTarget()
    {
        using var outputRoot = new TemporaryDirectory("ratools-retention-output");
        using var outsideRoot = new TemporaryDirectory("ratools-retention-outside");
        var sentinelPath = Path.Combine(outsideRoot.Path, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "must remain unchanged");
        var jobsRoot = Path.Combine(outputRoot.Path, ApplicationId.ToString("N"), "_jobs");
        Directory.CreateDirectory(jobsRoot);
        var staleLinkPath = Path.Combine(jobsRoot, Guid.NewGuid().ToString("N"));
        CreatePlatformDirectoryLink(staleLinkPath, outsideRoot.Path);
        Directory.SetLastWriteTimeUtc(outsideRoot.Path, DateTime.UtcNow.AddDays(-2));

        try
        {
            var before = CaptureFileSystemState(outsideRoot.Path);
            var writer = CreateWriter(outputRoot.Path, retainJobRuns: 1);

            await writer.SaveAsync(
                ApplicationId,
                "0001",
                Guid.NewGuid(),
                [new BackboneGeneratedFile("index.xml", "<ectd:ectd />")],
                "publish-report.json",
                "0001.zip",
                []);

            Assert.True(Directory.Exists(staleLinkPath));
            Assert.Equal(before, CaptureFileSystemState(outsideRoot.Path));
        }
        finally
        {
            DeleteDirectoryLink(staleLinkPath);
        }
    }

    private static ConfiguredWorkspacePathPolicy CreatePolicy(params string[] allowedRoots)
        => new(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = allowedRoots
        }));

    private static LocalBackboneFileWriter CreateWriter(string rootPath, int retainJobRuns = 0)
        => new(
            Options.Create(new BackboneOutputOptions
            {
                RootPath = rootPath,
                RetainJobRuns = retainJobRuns
            }),
            NullLogger<LocalBackboneFileWriter>.Instance);

    private static FileSystemEntryState[] CaptureFileSystemState(string rootPath)
        => Directory.EnumerateFileSystemEntries(rootPath, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var attributes = File.GetAttributes(path);
                var isDirectory = (attributes & FileAttributes.Directory) == FileAttributes.Directory;
                return new FileSystemEntryState(
                    Path.GetRelativePath(rootPath, path),
                    attributes,
                    isDirectory ? null : new FileInfo(path).Length,
                    File.GetLastWriteTimeUtc(path),
                    isDirectory ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();

    private static string ToggleAsciiCase(string value)
        => new(value.Select(character => char.IsAsciiLetterUpper(character)
            ? char.ToLowerInvariant(character)
            : char.IsAsciiLetterLower(character)
                ? char.ToUpperInvariant(character)
                : character).ToArray());

    private static void CreatePlatformDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start mklink for the Windows junction test.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to create Windows junction (exit {process.ExitCode}): {standardOutput} {standardError}");
        }
    }

    private static void DeleteDirectoryLink(string linkPath)
    {
        if (Directory.Exists(linkPath))
        {
            Directory.Delete(linkPath);
        }
    }

    private sealed record FileSystemEntryState(
        string RelativePath,
        FileAttributes Attributes,
        long? Length,
        DateTime LastWriteTimeUtc,
        string? Sha256);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
