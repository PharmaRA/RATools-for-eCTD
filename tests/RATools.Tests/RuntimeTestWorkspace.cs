using System.Runtime.CompilerServices;

namespace RATools.Tests;

internal static class RuntimeTestWorkspace
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // VSTest otherwise changes cwd to the output directory. Opt in only for
        // the retained local runner; ordinary test invocation keeps its behavior.
        var configured = Environment.GetEnvironmentVariable("RATOOLS_TEST_REPOSITORY_ROOT");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var root = Path.GetFullPath(configured);
        if (!File.Exists(Path.Combine(root, "global.json")) ||
            !File.Exists(Path.Combine(root, "tests", "RATools.Tests", "RATools.Tests.csproj")))
            throw new InvalidOperationException("The configured test repository root is not a RATools workspace.");
        for (var current = root; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The test repository root must not traverse a link.");
        Directory.SetCurrentDirectory(root);
    }
}
