using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using RATools.Application.DocumentProcessing;
using RATools.Infrastructure.DocumentProcessing;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfRenameEngineTests
{
    [Fact]
    public async Task RelativeUriRewritePreservesActionQueryAndFragmentWithReorderedObjects()
    {
        var root = JsonNode.Parse(await QpdfAsync(["--json", "--json-stream-data=inline", Source, "-"]))!.AsObject();
        var objects = root["qpdf"]![1]!.AsObject();
        var page = objects["obj:" + root["pages"]![0]!["object"]!.GetValue<string>()]!["value"]!.AsObject();
        var annotation = objects["obj:" + page["/Annots"]![0]!.GetValue<string>()]!["value"]!.AsObject();
        annotation["/A"] = new JsonObject { ["/S"] = "/URI", ["/URI"] = "u:old.pdf?view=fit#nameddest=overview" };
        root["qpdf"]![1] = new JsonObject(objects.Reverse().Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
        var directory = Path.GetFullPath(".artifacts/p507-uri-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var patch = Path.Combine(directory, "variant.json"); await File.WriteAllTextAsync(patch, root.ToJsonString());
        var source = Path.Combine(directory, "uri.pdf"); await QpdfAsync(["--update-from-json=" + patch, Source, source]);
        var inspection = await Engine().InspectAsync(source, 16 * 1024 * 1024);
        Assert.True(inspection.NavigationComplete, string.Join(";", inspection.IncompleteReasons));
        var output = Path.Combine(directory, "result.pdf");
        var edited = await Engine().ExecuteAsync(new(source, output,
            new(null, null, null, [new(1, "page[1]/annotation[1]/URI", "old.pdf?view=fit#nameddest=overview", "../m3/renamed.pdf?view=fit#nameddest=overview", true)]),
            TimeSpan.FromSeconds(30), 16 * 1024 * 1024, 16 * 1024 * 1024));
        Assert.True(edited.Succeeded, edited.ErrorMessage);
        var result = await Engine().InspectAsync(output, 16 * 1024 * 1024);
        var actual = result.Links.Single(item => item.ObjectPath == "page[1]/annotation[1]");
        Assert.Equal(PdfLinkTargetKind.ExternalUri, actual.Kind);
        Assert.Equal("../m3/renamed.pdf?view=fit#nameddest=overview", actual.Target);
        Assert.Equal(inspection.Links.Skip(1), result.Links.Skip(1));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferenceRewriteChangesOnlyFileSpecificationAndRetainsNamedOrPageDestination(bool pageTarget)
    {
        var engine = Engine();
        var source = Path.GetFullPath("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var original = await engine.InspectAsync(source, 16 * 1024 * 1024);
        var selected = original.Links.First(link => link.FileSpecification == "old.pdf");
        var directory = Path.GetFullPath(".artifacts/p507-engine-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (pageTarget)
        {
            var prepared = await engine.ExecuteAsync(new(source, Path.Combine(directory, "page-source.pdf"),
                new(null, null, [new(selected.SourcePageNumber, "old.pdf#page=2", PdfLinkTargetKind.RelativeRemotePdf, selected.ObjectPath, selected.Target)]),
                TimeSpan.FromSeconds(30), 16 * 1024 * 1024, 16 * 1024 * 1024));
            Assert.True(prepared.Succeeded, prepared.ErrorMessage);
            source = prepared.OutputPath; original = await engine.InspectAsync(source, 16 * 1024 * 1024);
            selected = original.Links.First(link => link.FileSpecification == "old.pdf");
            Assert.Equal(2, selected.TargetPageNumber);
        }
        var parameters = JsonSerializer.Deserialize<PdfProcessingParameters>(JsonSerializer.Serialize(new
        {
            PageMode = (string?)null, Bookmarks = (object?)null, Links = (object?)null,
            ReferenceEdits = new[] { new { selected.SourcePageNumber, selected.ObjectPath, OldReference = "old.pdf", NewReference = "renamed.pdf", IsUri = false } }
        }))!;
        var result = await engine.ExecuteAsync(new(source, Path.Combine(directory, "result.pdf"), parameters,
            TimeSpan.FromSeconds(30), 16 * 1024 * 1024, 16 * 1024 * 1024));
        Assert.True(result.Succeeded, result.ErrorMessage);
        var reopened = await engine.InspectAsync(result.OutputPath, 16 * 1024 * 1024);
        var actual = reopened.Links.Single(link => link.ObjectPath == selected.ObjectPath);
        Assert.Equal("renamed.pdf", actual.FileSpecification);
        Assert.Equal(selected.NamedDestination, actual.NamedDestination);
        Assert.Equal(selected.TargetPageNumber, actual.TargetPageNumber);
        Assert.Equal(selected.Kind, actual.Kind);
        Assert.Equal(original.Links.Where(link => link.ObjectPath != selected.ObjectPath), reopened.Links.Where(link => link.ObjectPath != selected.ObjectPath));
        Assert.Equal(original.Bookmarks, reopened.Bookmarks);
        Assert.Equal(original.NamedDestinations, reopened.NamedDestinations);
    }

    private static QpdfProcessingEngine Engine() => new(new QpdfProcessingOptions
    {
        ExecutablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF") ??
            Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe")
    });
    private static string Source => Path.GetFullPath("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
    private static async Task<string> QpdfAsync(string[] arguments)
    {
        var executable = Environment.GetEnvironmentVariable("RATOOLS_QPDF") ?? Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start(); var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        Assert.True(process.ExitCode == 0, await errors); return await output;
    }
}
