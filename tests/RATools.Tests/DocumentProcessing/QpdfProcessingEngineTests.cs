using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Diagnostics;
using RATools.Application.DocumentProcessing;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.DocumentProcessing;

public sealed class QpdfProcessingEngineTests
{
    [Fact]
    public async Task Execute_PreservesBinaryStreamWithBase64PlusCharacters()
    {
        var root = FindRepositoryRoot();
        var executablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.Combine(root, ".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        var directory = Path.Combine(root, ".artifacts/p5-06-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "binary-stream.pdf");
        var output = Path.Combine(directory, "updated.pdf");
        var bytes = new MemoryStream();
        void Write(string text) => bytes.Write(System.Text.Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        foreach (var body in new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Binary 4 0 R >> >>"
        })
        {
            offsets.Add(bytes.Position);
            Write($"{offsets.Count - 1} 0 obj\n{body}\nendobj\n");
        }
        offsets.Add(bytes.Position);
        Write("4 0 obj\n<< /Length 3 >>\nstream\n");
        bytes.Write([0xfb, 0xef, 0xbe]); // ++++ in qpdf's inline base64 stream data.
        Write("\nendstream\nendobj\n");
        var xref = bytes.Position;
        Write("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        await File.WriteAllBytesAsync(source, bytes.ToArray());
        var sourceJson = await ReadQpdfJsonAsync(executablePath, source);
        using var original = JsonDocument.Parse(sourceJson);
        Assert.Contains("++++", EnumeratePropertyValues(original.RootElement, "data"));

        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = executablePath });
        var result = await engine.ExecuteAsync(new(source, output, new("UseNone", null, null),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024));

        Assert.True(result.Succeeded, result.ErrorMessage);
        using var reopened = JsonDocument.Parse(await ReadQpdfJsonAsync(executablePath, output));
        Assert.Contains("++++", EnumeratePropertyValues(reopened.RootElement, "data"));
        Assert.Equal(bytes.ToArray(), await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public void VersionCheck_RejectsPrefixVersionFromFakeExecutableOutput()
    {
        var method = typeof(QpdfProcessingEngine).GetMethod("IsSupportedVersion", BindingFlags.Static | BindingFlags.NonPublic)!;
        var accepted = (bool)method.Invoke(null, ["qpdf version 12.4.10\r\n", "12.4.1"])!;
        Assert.False(accepted);
    }

    [Fact]
    public async Task Execute_OutputWriteFailureKillsAndAwaitsControlledHelper()
    {
        var root = FindRepositoryRoot();
        var executablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.Combine(root, ".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        Assert.True(File.Exists(executablePath), "qpdf 12.4.1 is required for this qualification test; set RATOOLS_QPDF to its executable.");
        var directory = Path.Combine(root, ".artifacts/p5-06-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var processIds = new List<int>();
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        {
            ExecutablePath = executablePath,
            OutputStreamFactory = _ => new ThrowingWriteStream(),
            ProcessStartedCallback = processIds.Add
        });
        var source = Path.Combine(root, "tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var output = Path.Combine(directory, "write-failure.pdf");
        var result = await engine.ExecuteAsync(new(source, output, new(null, null, null), TimeSpan.FromSeconds(10), 8 * 1024 * 1024, 1024 * 1024));
        Assert.False(result.Succeeded);
        Assert.Contains("Injected output write failure", result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(processIds.Count >= 3, "The engine should start qpdf for version, inspection, and output generation.");
        var outputPid = processIds[^1];
        var exited = false;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using var outputProcess = Process.GetProcessById(outputPid);
                exited = outputProcess.HasExited;
            }
            catch (ArgumentException)
            {
                exited = true;
            }
            if (exited) break;
            await Task.Delay(20);
        }
        Assert.True(exited);
    }

    [Theory]
    [InlineData("named")]
    [InlineData("page")]
    [InlineData("remote-named")]
    [InlineData("remote-page")]
    [InlineData("remote-page-fragment")]
    [InlineData("remote-nameddest")]
    [InlineData("uri")]
    public async Task Execute_ChangesSelectedTargetAndReopensWithIndependentInspector(string kind)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var executablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.Combine(repositoryRoot, ".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = executablePath });
        var outputPath = Path.Combine(repositoryRoot, ".artifacts/p5-06-engine-tests", Guid.NewGuid().ToString("N"), "result.pdf");
        var navigation = await engine.InspectAsync(sourcePath, 8 * 1024 * 1024);
        var original = navigation.Links.Single(link => link.ObjectPath == "page[1]/annotation[1]");
        var edit = kind switch
        {
            "named" => new PdfLinkEdit(1, "overview", PdfLinkTargetKind.NamedDestination, original.ObjectPath, original.Target),
            "page" => new PdfLinkEdit(1, "2", PdfLinkTargetKind.Page, original.ObjectPath, original.Target),
            "remote-named" => new PdfLinkEdit(1, "target.pdf#overview", PdfLinkTargetKind.RelativeRemotePdf, original.ObjectPath, original.Target),
            "remote-page" => new PdfLinkEdit(1, "target.pdf", PdfLinkTargetKind.RelativeRemotePdf, original.ObjectPath, original.Target),
            "remote-page-fragment" => new PdfLinkEdit(1, "target.pdf#page=2", PdfLinkTargetKind.RelativeRemotePdf, original.ObjectPath, original.Target),
            "remote-nameddest" => new PdfLinkEdit(1, "target.pdf#nameddest=overview", PdfLinkTargetKind.RelativeRemotePdf, original.ObjectPath, original.Target),
            _ => new PdfLinkEdit(1, "https://example.org/new", PdfLinkTargetKind.ExternalUri, original.ObjectPath, original.Target)
        };
        var result = await engine.ExecuteAsync(new(sourcePath, outputPath, new(null, null, [edit]), TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024));
        Assert.True(result.Succeeded, result.ErrorMessage);
        using var stream = File.OpenRead(outputPath);
        var facts = new PdfPigPackageInspector().Inspect(stream, "source.pdf", new PackageReadLimits(), CancellationToken.None);
        var changed = facts.Links.Single(link => link.ObjectPath.StartsWith(original.ObjectPath + "/", StringComparison.Ordinal)).Target;
        Assert.False(changed.Invalid);
        Assert.Null(changed.UnsupportedReason);
        if (kind == "named") Assert.Equal("overview", changed.NamedDestination);
        if (kind == "page") Assert.Equal(2, changed.PageNumber);
        if (kind == "remote-named") { Assert.Equal("target.pdf", changed.FileSpecification); Assert.Equal("overview", changed.NamedDestination); }
        if (kind == "remote-page") { Assert.Equal("target.pdf", changed.FileSpecification); Assert.Equal(1, changed.PageNumber); }
        if (kind == "remote-page-fragment") { Assert.Equal("target.pdf", changed.FileSpecification); Assert.Equal(2, changed.PageNumber); }
        if (kind == "remote-nameddest") { Assert.Equal("target.pdf", changed.FileSpecification); Assert.Equal("overview", changed.NamedDestination); }
        if (kind == "uri") Assert.Equal("https://example.org/new", changed.Uri);
        var reopened = await engine.InspectAsync(outputPath, 8 * 1024 * 1024);
        Assert.True(reopened.Links.Single(link => link.ObjectPath == original.ObjectPath).IsValid);
    }

    [Theory]
    [InlineData("stream")]
    [InlineData("resources")]
    [InlineData("geometry")]
    public async Task ValidateResult_RejectsTamperedPageVisualState(string mutation)
    {
        var root = FindRepositoryRoot();
        var executablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.Combine(root, ".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        var sourcePath = Path.Combine(root, "tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = executablePath });
        var source = JsonNode.Parse(await ReadQpdfJsonAsync(executablePath, sourcePath))!.AsObject();
        var result = JsonNode.Parse(source.ToJsonString())!.AsObject();
        var pageReference = result["pages"]![0]! ["object"]!.GetValue<string>();
        var page = result["qpdf"]![1]!["obj:" + pageReference]!["value"]!.AsObject();
        if (mutation == "resources")
        {
            page["/Resources"] = new JsonObject { ["/Tampered"] = "/Resource" };
        }
        else if (mutation == "geometry")
        {
            page["/MediaBox"] = new JsonArray(0, 0, 1, 1);
        }
        else
        {
            var contentReference = page["/Contents"]!.GetValue<string>();
            var content = result["qpdf"]![1]!["obj:" + contentReference]!.AsObject();
            content["stream"]!.AsObject()["data"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("tampered content"));
        }

        var method = typeof(QpdfProcessingEngine).GetMethod("ValidateResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var invocation = Assert.Throws<TargetInvocationException>(() => method.Invoke(engine,
            [source, result, new PdfProcessingParameters(null, null, null)]));
        Assert.IsType<InvalidOperationException>(invocation.InnerException);
    }

    [Fact]
    public async Task Execute_MatchesReopenedOriginalTargetForInternalAndRemoteExplicitPages()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(root, "tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var executablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.Combine(root, ".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = executablePath });
        var navigation = await engine.InspectAsync(sourcePath, 8 * 1024 * 1024);
        var internalSelector = "page[1]/annotation[1]";
        var internalOriginal = navigation.Links.Single(link => link.ObjectPath == internalSelector);
        var directory = Path.Combine(root, ".artifacts/p5-06-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var firstInternal = Path.Combine(directory, "internal-first.pdf");
        var firstResult = await engine.ExecuteAsync(new(sourcePath, firstInternal,
            new(null, null, [new(1, "2", PdfLinkTargetKind.Page, internalSelector, internalOriginal.Target)]),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024));
        Assert.True(firstResult.Succeeded, firstResult.ErrorMessage);
        var reopenedInternal = (await engine.InspectAsync(firstInternal, 8 * 1024 * 1024)).Links.Single(link => link.ObjectPath == internalSelector);
        var secondInternal = Path.Combine(directory, "internal-second.pdf");
        var secondResult = await engine.ExecuteAsync(new(firstInternal, secondInternal,
            new(null, null, [new(1, "overview", PdfLinkTargetKind.NamedDestination, internalSelector, reopenedInternal.Target)]),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024));
        Assert.True(secondResult.Succeeded, secondResult.ErrorMessage);

        var remoteSelector = "page[1]/annotation[2]";
        var remoteOriginal = navigation.Links.Single(link => link.ObjectPath == remoteSelector);
        var firstRemote = Path.Combine(directory, "remote-first.pdf");
        var remoteFirstResult = await engine.ExecuteAsync(new(sourcePath, firstRemote,
            new(null, null, [new(1, "target.pdf", PdfLinkTargetKind.RelativeRemotePdf, remoteSelector, remoteOriginal.Target, TargetVersionId: Guid.NewGuid())]),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024));
        Assert.True(remoteFirstResult.Succeeded, remoteFirstResult.ErrorMessage);
        var reopenedRemote = (await engine.InspectAsync(firstRemote, 8 * 1024 * 1024)).Links.Single(link => link.ObjectPath == remoteSelector);
        Assert.Equal(1, reopenedRemote.TargetPageNumber);
        var secondRemote = Path.Combine(directory, "remote-second.pdf");
        var remoteSecondResult = await engine.ExecuteAsync(new(firstRemote, secondRemote,
            new(null, null, [new(1, "target.pdf#overview", PdfLinkTargetKind.RelativeRemotePdf, remoteSelector, reopenedRemote.Target, TargetVersionId: Guid.NewGuid())]),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024));
        Assert.True(remoteSecondResult.Succeeded, remoteSecondResult.ErrorMessage);
    }

    [Fact]
    public async Task Inspection_DoesNotResolveRemoteNamesAgainstLocalDestinations()
    {
        var root = FindRepositoryRoot();
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        { ExecutablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF") ?? Path.Combine(root, ".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe") });
        var navigation = await engine.InspectAsync(Path.Combine(root, "tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf"), 8 * 1024 * 1024);
        var remote = Assert.Single(navigation.Links, link => link.Kind == PdfLinkTargetKind.RelativeRemotePdf);
        Assert.Equal("overview", remote.NamedDestination);
        Assert.Null(remote.TargetPageNumber);
    }

    [Fact]
    public async Task Execute_OversizedOutputNeverAppearsAtResultPath()
    {
        var root = FindRepositoryRoot();
        var source = Path.Combine(root, "tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var output = Path.Combine(root, ".artifacts/p5-06-engine-tests", Guid.NewGuid().ToString("N"), "too-large.pdf");
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        { ExecutablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF") ?? Path.Combine(root, ".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe") });
        var result = await engine.ExecuteAsync(new(source, output, new("UseOutlines", null, null), TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 32));
        Assert.False(result.Succeeded);
        Assert.Equal("OUTPUT_SIZE_LIMIT", result.ErrorMessage);
        Assert.False(File.Exists(output));
        var taskDirectory = Directory.GetParent(output)!.FullName;
        var staged = Directory.GetFiles(taskDirectory, "*.qpdf.tmp");
        Assert.NotEmpty(staged);
        Assert.All(staged, path => Assert.InRange(new FileInfo(path).Length, 0, 32));
    }

    [Fact]
    public async Task Execute_UsesRealQpdfAndKeepsSourceBytesUntouched()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "tests", "RATools.Tests", "Fixtures", "PdfProcessing", "navigation-source.pdf");
        var executablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.Combine(repositoryRoot, ".artifacts", "tools", "qpdf-12.4.1", "qpdf-12.4.1-msvc64", "bin", "qpdf.exe");
        Assert.True(File.Exists(executablePath), "qpdf 12.4.1 is required for this qualification test; set RATOOLS_QPDF to its executable.");
        var outputDirectory = Path.Combine(repositoryRoot, ".artifacts", "p5-06-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "result.pdf");
        var before = await File.ReadAllBytesAsync(sourcePath);
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        {
            ExecutablePath = executablePath
        });
        var sourceNavigation = await engine.InspectAsync(sourcePath, 8 * 1024 * 1024, CancellationToken.None);
        Assert.Equal(2, sourceNavigation.PageCount);
        Assert.Equal("UseNone", sourceNavigation.PageMode);
        Assert.Equal(["Original heading", "Original child"], sourceNavigation.Bookmarks.Select(item => item.Title).ToArray());
        Assert.Equal([0, 1], sourceNavigation.Bookmarks.Select(item => item.Level).ToArray());
        Assert.Contains(sourceNavigation.Links, item => item.ObjectPath == "page[1]/annotation[1]");
        Assert.Contains(sourceNavigation.Links, item => item.ObjectPath == "page[1]/annotation[2]");
        Assert.Contains(sourceNavigation.NamedDestinations, item => item.Name == "overview" && item.PageNumber == 1);

        var result = await engine.ExecuteAsync(new PdfEngineRequest(
            sourcePath,
            outputPath,
            new PdfProcessingParameters("UseOutlines", [new("更新后的标题", 1, null, 0), new("Original child", 1, null, 1)],
                [new(1, "2", PdfLinkTargetKind.Page, "page[1]/annotation[2]")]),
            TimeSpan.FromSeconds(30),
            8 * 1024 * 1024,
            64 * 1024), CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(before, await File.ReadAllBytesAsync(sourcePath));
        Assert.NotEqual(before, await File.ReadAllBytesAsync(outputPath));
        var json = await ReadQpdfJsonAsync(executablePath, outputPath);
        using (var parsed = JsonDocument.Parse(json))
        {
            var titles = EnumeratePropertyValues(parsed.RootElement, "title").ToArray();
            Assert.Contains("更新后的标题", titles);
            Assert.Contains("Original child", titles);
        }
        using var resultStream = File.OpenRead(outputPath);
        var facts = new PdfPigPackageInspector().Inspect(resultStream, "m5/navigation-source.pdf", new PackageReadLimits(), CancellationToken.None);
        using var sourceStream = File.OpenRead(sourcePath);
        var sourceFacts = new PdfPigPackageInspector().Inspect(sourceStream, "m5/navigation-source.pdf", new PackageReadLimits(), CancellationToken.None);
        Assert.Equal(2, facts.Basics.PageCount);
        Assert.Equal(sourceFacts.Basics.PageCount, facts.Basics.PageCount);
        Assert.Equal(sourceFacts.Basics.HasSearchableText, facts.Basics.HasSearchableText);
        Assert.Equal(sourceFacts.Basics.Links.Count, facts.Basics.Links.Count);
        Assert.Equal("UseOutlines", facts.Basics.PageMode);
        var resultNavigation = await engine.InspectAsync(outputPath, 8 * 1024 * 1024, CancellationToken.None);
        Assert.Equal(2, resultNavigation.Links.Single(item => item.ObjectPath == "page[1]/annotation[2]").TargetPageNumber);
        Assert.Equal(PdfLinkTargetKind.NamedDestination, resultNavigation.Links.Single(item => item.ObjectPath == "page[1]/annotation[1]").Kind);
    }

    [Fact]
    public async Task Execute_ReplacesBookmarkTreeAndCanClearItExplicitly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "tests", "RATools.Tests", "Fixtures", "PdfProcessing", "navigation-source.pdf");
        var executablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.Combine(repositoryRoot, ".artifacts", "tools", "qpdf-12.4.1", "qpdf-12.4.1-msvc64", "bin", "qpdf.exe");
        Assert.True(File.Exists(executablePath), "qpdf 12.4.1 is required for this qualification test; set RATOOLS_QPDF to its executable.");
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = executablePath });
        var outputDirectory = Path.Combine(repositoryRoot, ".artifacts", "p5-06-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var replacementPath = Path.Combine(outputDirectory, "replacement.pdf");
        var replacement = await engine.ExecuteAsync(new PdfEngineRequest(sourcePath, replacementPath,
            new PdfProcessingParameters(null,
                [new("Root A", 1, null, 0), new("Child B", 2, null, 1), new("Root C", null, "overview", 0)], null),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024), CancellationToken.None);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        var replacedNavigation = await engine.InspectAsync(replacementPath, 8 * 1024 * 1024, CancellationToken.None);
        Assert.Equal(["Root A", "Child B", "Root C"], replacedNavigation.Bookmarks.Select(item => item.Title).ToArray());
        Assert.Equal([0, 1, 0], replacedNavigation.Bookmarks.Select(item => item.Level).ToArray());
        Assert.Equal(1, replacedNavigation.Bookmarks[2].PageNumber);

        var clearedPath = Path.Combine(outputDirectory, "cleared.pdf");
        var cleared = await engine.ExecuteAsync(new PdfEngineRequest(sourcePath, clearedPath,
            new PdfProcessingParameters(null, [], null), TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024), CancellationToken.None);
        Assert.True(cleared.Succeeded, cleared.ErrorMessage);
        Assert.Empty((await engine.InspectAsync(clearedPath, 8 * 1024 * 1024, CancellationToken.None)).Bookmarks);
    }

    [Fact]
    public async Task Execute_RejectsUnknownDestinationAmbiguousSelectorAndExistingOutput()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "tests", "RATools.Tests", "Fixtures", "PdfProcessing", "navigation-source.pdf");
        var executablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.Combine(repositoryRoot, ".artifacts", "tools", "qpdf-12.4.1", "qpdf-12.4.1-msvc64", "bin", "qpdf.exe");
        Assert.True(File.Exists(executablePath), "qpdf 12.4.1 is required for this qualification test; set RATOOLS_QPDF to its executable.");
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = executablePath });
        var outputDirectory = Path.Combine(repositoryRoot, ".artifacts", "p5-06-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var unknownDestination = Path.Combine(outputDirectory, "unknown.pdf");
        var rejectedDestination = await engine.ExecuteAsync(new PdfEngineRequest(sourcePath, unknownDestination,
            new PdfProcessingParameters(null, [new("Broken", null, "missing", 0)], null),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024), CancellationToken.None);
        Assert.False(rejectedDestination.Succeeded);
        Assert.Contains("Named destination 'missing'", rejectedDestination.ErrorMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(unknownDestination));

        var ambiguous = Path.Combine(outputDirectory, "ambiguous.pdf");
        var rejectedSelector = await engine.ExecuteAsync(new PdfEngineRequest(sourcePath, ambiguous,
            new PdfProcessingParameters(null, null, [new(1, "2", PdfLinkTargetKind.Page, "page[1]/annotation[3]")]),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024), CancellationToken.None);
        Assert.False(rejectedSelector.Succeeded);
        Assert.Contains("annotation list", rejectedSelector.ErrorMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(ambiguous));

        var existing = Path.Combine(outputDirectory, "existing.pdf");
        await File.WriteAllBytesAsync(existing, [1, 2, 3]);
        var rejectedExisting = await engine.ExecuteAsync(new PdfEngineRequest(sourcePath, existing,
            new PdfProcessingParameters(null, null, null), TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 64 * 1024), CancellationToken.None);
        Assert.False(rejectedExisting.Succeeded);
        Assert.Equal("OUTPUT_EXISTS", rejectedExisting.ErrorMessage);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(existing));
    }

    private static async Task<string> ReadQpdfJsonAsync(string executablePath, string pdfPath)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add("--json");
        process.StartInfo.ArgumentList.Add("--json-stream-data=inline");
        process.StartInfo.ArgumentList.Add(pdfPath);
        process.StartInfo.ArgumentList.Add("-");
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        return output;
    }

    private static IEnumerable<string> EnumeratePropertyValues(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                    yield return property.Value.GetString()!;
                foreach (var value in EnumeratePropertyValues(property.Value, propertyName)) yield return value;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray())
                foreach (var nested in EnumeratePropertyValues(value, propertyName)) yield return nested;
        }
    }

    private static string FindRepositoryRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null && !(Directory.Exists(Path.Combine(candidate.FullName, "src")) &&
            Directory.Exists(Path.Combine(candidate.FullName, "tests")))) candidate = candidate.Parent;
        return candidate?.FullName ?? throw new InvalidOperationException("Repository root was not found for the qualification fixture.");
    }

    private sealed class ThrowingWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Injected output write failure");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("Injected output write failure"));
    }
}
