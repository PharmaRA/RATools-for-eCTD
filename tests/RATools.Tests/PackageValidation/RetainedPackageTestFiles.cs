namespace RATools.Tests.PackageValidation;

// Only roots created here may be moved. Evidence lives outside source/capture
// trees, so a missing-file scenario cannot accidentally add a package input.
internal static class RetainedPackageTestFiles
{
    private static string Parent => Path.GetFullPath(Path.Combine(PackageRuleCatalogTests.Root, ".artifacts", "package-regression"));
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Create()
    {
        var root = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        EnsureNoLinks(root);
        Directory.CreateDirectory(root);
        return root;
    }

    public static void RetainDirectory(string root)
    {
        ValidateRoot(root);
        if (!Directory.Exists(root)) return;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var path))
        {
            EnsureNoLinks(path);
            if (Directory.Exists(path))
                foreach (var child in Directory.EnumerateFileSystemEntries(path)) pending.Push(child);
        }
        var destination = Destination("_recycle", Path.GetFileName(root));
        Directory.Move(root, destination);
        Assert.False(Directory.Exists(root));
        Assert.True(Directory.Exists(destination));
    }

    public static void RetainFile(string root, string path)
    {
        ValidateChild(root, path);
        EnsureNoLinks(path);
        var original = File.ReadAllBytes(path);
        var destination = Destination("_recycle", Path.GetFileName(path));
        File.Move(path, destination);
        Assert.False(File.Exists(path));
        Assert.Equal(original, File.ReadAllBytes(destination));
    }

    public static void RetainReviewedLink(string root, string link, string expectedTarget)
    {
        ValidateChild(root, link); ValidateChild(root, expectedTarget);
        EnsureNoLinks(Path.GetDirectoryName(link)!); EnsureNoLinks(expectedTarget);
        var info = new DirectoryInfo(link);
        Assert.True((info.Attributes & FileAttributes.ReparsePoint) != 0);
        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        Assert.NotNull(target);
        Assert.True(string.Equals(Path.GetFullPath(expectedTarget), target.FullName, Comparison));
        var linkTarget = info.LinkTarget;
        var destination = Destination("_reviewed-links", "link");
        Directory.Move(link, destination);
        Assert.Equal(linkTarget, new DirectoryInfo(destination).LinkTarget);
        Assert.True(Directory.Exists(expectedTarget));
    }

    public static void AssertOnlyRecycledCaptures(string captureParent)
    {
        EnsureNoLinks(captureParent);
        var recycle = Assert.Single(Directory.GetFileSystemEntries(captureParent));
        Assert.Equal(Path.Combine(captureParent, "_recycle"), recycle);
        EnsureNoLinks(recycle);
        var captures = Directory.GetFileSystemEntries(recycle);
        Assert.NotEmpty(captures);
        foreach (var capture in captures)
        {
            EnsureNoLinks(capture); Assert.True(Directory.Exists(capture));
            var name = Path.GetFileName(capture);
            Assert.Equal(71, name.Length); Assert.StartsWith("input-", name, StringComparison.Ordinal);
            Assert.True(Guid.TryParseExact(name[6..38], "N", out _)); Assert.Equal('-', name[38]);
            Assert.True(Guid.TryParseExact(name[39..], "N", out _));
            foreach (var file in Directory.GetFileSystemEntries(capture))
            {
                EnsureNoLinks(file); Assert.True(File.Exists(file));
                Assert.True(Guid.TryParseExact(Path.GetFileName(file), "N", out _));
            }
        }
    }

    public static string[] CaptureFingerprints(string captureParent) => Directory.EnumerateFiles(captureParent, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetFileName(file) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))))
        .Order(StringComparer.Ordinal).ToArray();

    private static void ValidateChild(string root, string path)
    {
        ValidateRoot(root);
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, Comparison))
            throw new InvalidOperationException("Fixture path escaped its owned root.");
    }

    private static void ValidateRoot(string root)
    {
        root = Path.GetFullPath(root);
        if (!string.Equals(Path.GetDirectoryName(root), Parent, Comparison) || !Guid.TryParseExact(Path.GetFileName(root), "N", out _))
            throw new InvalidOperationException("Unexpected package fixture ownership.");
        EnsureNoLinks(root);
    }

    private static string Destination(string category, string name)
    {
        var recycle = Path.Combine(Parent, category);
        EnsureNoLinks(recycle); Directory.CreateDirectory(recycle); EnsureNoLinks(recycle);
        var destination = Path.GetFullPath(Path.Combine(recycle, Guid.NewGuid().ToString("N") + "-" + name));
        if (!string.Equals(Path.GetDirectoryName(destination), recycle, Comparison))
            throw new InvalidOperationException("Unexpected package fixture recycle destination.");
        return destination;
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Fixture links require explicit review before retention.");
    }
}
