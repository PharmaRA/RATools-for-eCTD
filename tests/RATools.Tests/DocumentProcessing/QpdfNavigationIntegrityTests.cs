using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using RATools.Application.DocumentProcessing;
using RATools.Infrastructure.DocumentProcessing;

namespace RATools.Tests.DocumentProcessing;

public sealed class QpdfNavigationIntegrityTests
{
    [Fact]
    public async Task EditingInitialViewPreservesIndirectAnnotationReferences()
    {
        var output = Output("indirect-preserved.pdf");
        var result = await Engine().ExecuteAsync(new(Source, output, new("UseOutlines", null, null),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 1024 * 1024));
        Assert.True(result.Succeeded, result.ErrorMessage);
        var root = await ReadAsync(output);
        var objects = root["qpdf"]![1]!.AsObject();
        var page = objects["obj:" + root["pages"]![0]!["object"]!.GetValue<string>()]!["value"]!;
        Assert.All(page["/Annots"]!.AsArray(), token => Assert.Equal(System.Text.Json.JsonValueKind.String, token!.GetValueKind()));
    }

    [Fact]
    public async Task UneditedLinkToTheEditedPageDoesNotCauseAFalseAppearanceMismatch()
    {
        var root = await ReadAsync(Source);
        var objects = root["qpdf"]![1]!.AsObject();
        var pageReference = root["pages"]![0]!["object"]!.GetValue<string>();
        var page = objects["obj:" + pageReference]!["value"]!;
        var annotation = objects["obj:" + page["/Annots"]![1]!.GetValue<string>()]!["value"]!;
        annotation["/A"] = new JsonObject { ["/S"] = "/GoTo", ["/D"] = new JsonArray(pageReference, "/Fit") };
        var source = Output("self-page.pdf");
        await File.WriteAllTextAsync(source + ".json", root.ToJsonString());
        await QpdfAsync(["--update-from-json=" + source + ".json", Source, source]);
        var result = await Engine().ExecuteAsync(new(source, Output("changed.pdf"), new(null, null,
            [new(1, "2", PdfLinkTargetKind.Page, "page[1]/annotation[1]")]),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 1024 * 1024));
        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    [Theory]
    [InlineData("PAGE=2", "PAGE=2")]
    [InlineData("NAMEDDEST=overview", "NAMEDDEST=overview")]
    [InlineData("nameddest=page=2", "page=2")]
    public async Task RemoteNamedDestinationsAreNotReinterpretedAsPageInstructions(string fragment, string expectedName)
    {
        var output = Output("named.pdf");
        var result = await Engine().ExecuteAsync(new(Source, output,
            new(null, null, [new(1, "target.pdf#" + fragment, PdfLinkTargetKind.RelativeRemotePdf, "page[1]/annotation[1]")]),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 1024 * 1024));
        Assert.True(result.Succeeded, result.ErrorMessage);
        var navigation = await Engine().InspectAsync(output, 8 * 1024 * 1024);
        var link = navigation.Links.Single(item => item.ObjectPath == "page[1]/annotation[1]");
        Assert.Equal(expectedName, link.NamedDestination);
        Assert.Null(link.TargetPageNumber);
    }

    [Fact]
    public async Task IndirectArraysActionsNameTreesAndDestinationDictionariesRemainEditable()
    {
        var root = await ReadAsync(Source);
        var objects = root["qpdf"]![1]!.AsObject();
        var nextId = root["qpdf"]![0]!["maxobjectid"]!.GetValue<int>();
        string Indirect(JsonNode value)
        {
            var reference = $"{++nextId} 0 R";
            objects["obj:" + reference] = new JsonObject { ["value"] = value.DeepClone() };
            return reference;
        }
        var catalog = objects.Select(item => item.Value?["value"]).OfType<JsonObject>()
            .Single(item => item["/Type"]?.ToString() == "/Catalog");
        var firstPage = objects["obj:" + root["pages"]![0]!["object"]!.GetValue<string>()]!["value"]!.AsObject();
        var annotations = firstPage["/Annots"]!.AsArray();
        var link = objects["obj:" + annotations[0]!.GetValue<string>()]!["value"]!.AsObject();
        link["/A"] = Indirect(link["/A"]!);
        firstPage["/Annots"] = Indirect(annotations);
        var pageReference = root["pages"]![0]!["object"]!.GetValue<string>();
        var destination = Indirect(new JsonObject { ["/D"] = Indirect(new JsonArray(pageReference, "/Fit")) });
        catalog.Remove("/Dests");
        catalog["/Names"] = Indirect(new JsonObject { ["/Dests"] = Indirect(new JsonObject {
            ["/Names"] = new JsonArray("u:overview", destination) }) });
        root["qpdf"]![0]!["maxobjectid"] = nextId;
        var source = Output("indirect.pdf");
        var patch = source + ".json";
        await File.WriteAllTextAsync(patch, root.ToJsonString());
        await QpdfAsync(["--update-from-json=" + patch, Source, source]);
        var inspection = await Engine().InspectAsync(source, 8 * 1024 * 1024);
        Assert.True(inspection.NavigationComplete, string.Join(";", inspection.IncompleteReasons));
        Assert.Contains(inspection.NamedDestinations, item => item.Name == "overview" && item.PageNumber == 1 && item.IsValid);
        var result = await Engine().ExecuteAsync(new(source, Output("edited.pdf"), new("UseOutlines", null,
            [new(1, "2", PdfLinkTargetKind.Page, "page[1]/annotation[1]", "overview")]),
            TimeSpan.FromSeconds(30), 8 * 1024 * 1024, 1024 * 1024));
        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    [Theory]
    [InlineData("annotation")]
    [InlineData("bookmarks")]
    [InlineData("unselected-link")]
    [InlineData("page-mode")]
    public async Task ResultVerificationRejectsChangesOutsideTheExplicitNavigationEdits(string mutation)
    {
        var source = await ReadAsync(Source);
        var result = source.DeepClone().AsObject();
        var objects = result["qpdf"]![1]!.AsObject();
        var page = objects["obj:" + result["pages"]![0]!["object"]!.GetValue<string>()]!["value"]!.AsObject();
        var catalog = objects.Select(item => item.Value?["value"]).OfType<JsonObject>()
            .Single(item => item["/Type"]?.ToString() == "/Catalog");
        switch (mutation)
        {
            case "annotation":
                var annotation = objects["obj:" + page["/Annots"]![0]!.GetValue<string>()]!["value"]!.AsObject();
                annotation["/Rect"] = new JsonArray(0, 0, 1, 1);
                break;
            case "bookmarks": result["outlines"] = new JsonArray(); catalog.Remove("/Outlines"); break;
            case "unselected-link": page["/Annots"]!.AsArray().RemoveAt(1); break;
            case "page-mode": catalog["/PageMode"] = "/FullScreen"; break;
        }
        var method = typeof(QpdfProcessingEngine).GetMethod("ValidateResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(Engine(), [source, result, new PdfProcessingParameters(null, null, null)]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    private static string Source => Path.GetFullPath("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
    private static string Executable => Environment.GetEnvironmentVariable("RATOOLS_QPDF")
        ?? Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
    private static QpdfProcessingEngine Engine() => new(new QpdfProcessingOptions { ExecutablePath = Executable });
    private static string Output(string name)
    {
        var directory = Path.GetFullPath(".artifacts/p506-integrity-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }
    private static async Task<JsonObject> ReadAsync(string path) => JsonNode.Parse(await QpdfAsync(["--json", "--json-stream-data=inline", path, "-"]))!.AsObject();
    private static async Task<string> QpdfAsync(string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(Executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
