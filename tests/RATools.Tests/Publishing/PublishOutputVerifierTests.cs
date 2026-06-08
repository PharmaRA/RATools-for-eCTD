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

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

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
