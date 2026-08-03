using System.IO.Compression;
using RATools.Application.Publishing;

namespace RATools.Tests.Publishing;

public sealed class PublishOutputVerifierTests
{
    [Fact]
    public async Task VerifyAsync_ReturnsArtifactEvidenceAndNoFindings_WhenOutputIsComplete()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1", "us", "11-forms"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var leafPath = Path.Combine(outputDir, "m1", "us", "11-forms", "leaf.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(leafPath, "leaf");
            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/us/11-forms/leaf.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.True(result.Summary.IsConsistent);
            Assert.Empty(result.Evidence.Findings);
            Assert.Contains(result.Evidence.Artifacts, x => x.Role == "BackboneXml" && x.Exists && x.ZipEntryPresent == true);
            Assert.Contains(result.Evidence.Artifacts, x => x.Role == "PublishReport" && x.Exists && x.ZipEntryPresent is null);
            Assert.Contains(result.Evidence.Artifacts, x => x.Role == "PackageZip" && x.Exists && x.ZipEntryPresent is null);
            Assert.Contains(result.Evidence.Artifacts, x => x.RelativePath == "m1/us/11-forms/leaf.pdf" && x.Role == "OutputFile" && x.ZipEntryPresent == true);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsMissingReferencedFile()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(outputDir);
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/us/11-forms/missing.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Equal(1, result.Summary.MissingFilesCount);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "MissingReferencedFile" && x.Path == "m1/us/11-forms/missing.pdf");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReadsReferencesFromDtdCompatibleXlinkNamespace()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(outputDir);
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(backbonePath, DtdCompatibleBackboneXml("m1/us/12-cover-letters/missing.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Equal(1, result.Summary.MissingFilesCount);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "MissingReferencedFile" && x.Path == "m1/us/12-cover-letters/missing.pdf");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsMissingZipEntry()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var leafPath = Path.Combine(outputDir, "m1", "leaf.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/leaf.pdf"));
            await File.WriteAllTextAsync(leafPath, "leaf");
            await File.WriteAllTextAsync(reportPath, "{}");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("index.xml");
            }

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Equal(1, result.Summary.MissingZipEntriesCount);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "MissingZipEntry" && x.Path == "m1/leaf.pdf");
            Assert.Contains(result.Evidence.Artifacts, x => x.RelativePath == "m1/leaf.pdf" && x.ZipEntryPresent == false);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsMissingZipEntryForBackboneXml()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var leafPath = Path.Combine(outputDir, "m1", "leaf.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/leaf.pdf"));
            await File.WriteAllTextAsync(leafPath, "leaf");
            await File.WriteAllTextAsync(reportPath, "{}");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("m1/leaf.pdf");
            }

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Equal(1, result.Summary.MissingZipEntriesCount);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "MissingZipEntry" && x.Path == "index.xml");
            Assert.Contains(result.Evidence.Artifacts, x => x.Role == "BackboneXml" && x.RelativePath == "index.xml" && x.ZipEntryPresent == false);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsInvalidZip()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(outputDir);
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(backbonePath, BackboneXml("leaf.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            await File.WriteAllTextAsync(packagePath, "not a zip");

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Equal(1, result.Summary.MismatchedArtifactsCount);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "InvalidZip" && x.Path == packagePath);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsMissingOutputDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var backbonePath = Path.Combine(root, "missing-output", "index.xml");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");
            await File.WriteAllTextAsync(reportPath, "{}");
            await File.WriteAllTextAsync(packagePath, "not checked because output missing");

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "OutputDirectoryMissing");
            Assert.Contains(result.Evidence.Findings, x => x.Type == "MissingTopLevelArtifact" && x.Path == backbonePath);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsChecksumMismatchAgainstDeclaredMd5()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var leafPath = Path.Combine(outputDir, "m1", "leaf.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(leafPath, "actual payload");
            await File.WriteAllTextAsync(backbonePath, BackboneXmlWithChecksum("m1/leaf.pdf", new string('0', 32)));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "ChecksumMismatch" && x.Path == "m1/leaf.pdf");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_AcceptsMatchingDeclaredMd5()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var leafPath = Path.Combine(outputDir, "m1", "leaf.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(leafPath, "actual payload");
            var actualMd5 = ComputeMd5(leafPath);
            await File.WriteAllTextAsync(backbonePath, BackboneXmlWithChecksum("m1/leaf.pdf", actualMd5));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.True(result.Summary.IsConsistent);
            Assert.DoesNotContain(result.Evidence.Findings, x => x.Type == "ChecksumMismatch");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_FlagsOrphanFilesNotReferencedByAnyBackbone()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var referencedPath = Path.Combine(outputDir, "m1", "leaf.pdf");
            var orphanPath = Path.Combine(outputDir, "m1", "orphan.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(referencedPath, "leaf");
            await File.WriteAllTextAsync(orphanPath, "orphan");
            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/leaf.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            // 孤儿是 Warning 级：不破坏 isConsistent，但必须可见。
            Assert.True(result.Summary.IsConsistent);
            var orphanFinding = Assert.Single(result.Evidence.Findings, x => x.Type == "OrphanFile");
            Assert.Equal("Warning", orphanFinding.Severity);
            Assert.Equal("m1/orphan.pdf", orphanFinding.Path);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_TreatsRegionalBackboneReferencesAsNonOrphans()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1", "us", "11-forms"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var regionalPath = Path.Combine(outputDir, "m1", "us", "us-regional.xml");
            var formPath = Path.Combine(outputDir, "m1", "us", "11-forms", "form.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(formPath, "form");
            // form.pdf 只被区域 backbone 引用（href 相对 m1/us/），index.xml 不引用它。
            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/us/us-regional.xml"));
            await File.WriteAllTextAsync(regionalPath, BackboneXml("11-forms/form.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.DoesNotContain(result.Evidence.Findings, x => x.Type == "OrphanFile");
            Assert.DoesNotContain(result.Evidence.Findings, x => x.Type == "MissingReferencedFile");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsMissingDtdAssetReferencedByDoctype()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(outputDir);
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            var xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <!DOCTYPE ectd:ectd SYSTEM "util/dtd/ich-ectd-3-2.dtd">
                <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3.org/1999/xlink" />
                """;
            await File.WriteAllTextAsync(backbonePath, xml);
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "MissingDtdAsset" && x.Path == "util/dtd/ich-ectd-3-2.dtd");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsIndexMd5Mismatch()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(outputDir);
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(backbonePath, BackboneXml("index.xml"));
            await File.WriteAllTextAsync(Path.Combine(outputDir, "index-md5.txt"), $"{new string('f', 32)}  index.xml\n");
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.False(result.Summary.IsConsistent);
            Assert.Contains(result.Evidence.Findings, x => x.Type == "IndexMd5Mismatch");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_IgnoresCrossSequenceReferencesOutsidePackageRoot()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(outputDir);
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            // modified-file 风格的跨序列引用（../0000/…）逃出包根，不属于本包核验范围。
            await File.WriteAllTextAsync(backbonePath, BackboneXml("../0000/m1/old.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            Assert.True(result.Summary.IsConsistent);
            Assert.DoesNotContain(result.Evidence.Findings, x => x.Type == "MissingReferencedFile");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_FlagsEmptyFolderAsWarning()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1"));
            Directory.CreateDirectory(Path.Combine(outputDir, "m2"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var leafPath = Path.Combine(outputDir, "m1", "leaf.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(leafPath, "leaf");
            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/leaf.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            // 空文件夹与孤儿同为 Warning：可见但不阻断已成功的发布。
            Assert.True(result.Summary.IsConsistent);
            var finding = Assert.Single(result.Evidence.Findings, x => x.Type == "EmptyFolder");
            Assert.Equal("Warning", finding.Severity);
            Assert.Equal("m2", finding.Path);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_DoesNotFlagDirectoriesThatContainFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1", "us", "11-forms"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var leafPath = Path.Combine(outputDir, "m1", "us", "11-forms", "form.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(leafPath, "form");
            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/us/11-forms/form.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            // 中间层目录本身没有文件，但子树有——不该报。
            Assert.DoesNotContain(result.Evidence.Findings, x => x.Type == "EmptyFolder");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsOnlyOutermostDirectoryForNestedEmptyFolders()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "m1"));
            Directory.CreateDirectory(Path.Combine(outputDir, "m3", "32-body-of-data", "empty-leaf-dir"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var leafPath = Path.Combine(outputDir, "m1", "leaf.pdf");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(leafPath, "leaf");
            await File.WriteAllTextAsync(backbonePath, BackboneXml("m1/leaf.pdf"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            var finding = Assert.Single(result.Evidence.Findings, x => x.Type == "EmptyFolder");
            Assert.Equal("m3", finding.Path);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task VerifyAsync_FlagsEmptyDirectoryUnderUtilEvenThoughUtilIsOrphanWhitelisted()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(outputDir, "util", "dtd"));
            Directory.CreateDirectory(Path.Combine(outputDir, "util", "style"));
            var backbonePath = Path.Combine(outputDir, "index.xml");
            var dtdPath = Path.Combine(outputDir, "util", "dtd", "ich-ectd-3-2.dtd");
            var reportPath = Path.Combine(root, "publish-report.json");
            var packagePath = Path.Combine(root, "package.zip");

            await File.WriteAllTextAsync(dtdPath, "<!-- dtd -->");
            await File.WriteAllTextAsync(backbonePath, BackboneXml("util/dtd/ich-ectd-3-2.dtd"));
            await File.WriteAllTextAsync(reportPath, "{}");
            CreateZip(packagePath, outputDir);

            var result = await new PublishOutputVerifier().VerifyAsync(backbonePath, reportPath, packagePath);

            // util/ 只在孤儿扫描里被白名单，空目录检查对它同样生效。
            var finding = Assert.Single(result.Evidence.Findings, x => x.Type == "EmptyFolder");
            Assert.Equal("util/style", finding.Path);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string BackboneXmlWithChecksum(string href, string md5) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3.org/1999/xlink">
          <ectd:leaf xlink:href="{href}" checksum="{md5}" checksum-type="md5" />
        </ectd:ectd>
        """;

    private static string BackboneXml(string href) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3.org/1999/xlink">
          <ectd:leaf xlink:href="{href}" />
        </ectd:ectd>
        """;

    private static string DtdCompatibleBackboneXml(string href) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3c.org/1999/xlink">
          <ectd:leaf xlink:href="{href}" />
        </ectd:ectd>
        """;

    private static void CreateZip(string packagePath, string sourceDirectory)
    {
        ZipFile.CreateFromDirectory(sourceDirectory, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
