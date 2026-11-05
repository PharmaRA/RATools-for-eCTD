using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RATools.Api.Controllers;
using RATools.Api.Middleware;
using RATools.Application.Assembly;
using RATools.Application.DocumentProcessing;
using RATools.Application.PackageValidation;
using RATools.Domain.Applications;
using RATools.Infrastructure.Assembly;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.Assembly;

public sealed class AssemblyHttpTests
{
    [Fact]
    public async Task MoreThanThousandRetainedSourcesRemainRecoverableThroughBoundedStablePages()
    {
        var rig = await AssemblyRig.CreateAsync();
        for (var index = 0; index < 1001; index++)
            await rig.Documents.AddAsync(new($"retained-{index}.pdf", "application/pdf", rig.Original.Length, rig.Document.Sha256, rig.Document.Md5,
                Path.Combine(rig.Root, "0001", "_assembly-sources", index.ToString(System.Globalization.CultureInfo.InvariantCulture), $"retained-{index}.pdf")));
        await using var host = CreateHost(rig, new AssemblyExecutionOptions(), null, startWorker: false); await host.StartAsync();
        try
        {
            using var client = Client(host); var prefix = Prefix(rig.Application.Id) + "/assembly-sources";
            var first = (await client.GetFromJsonAsync<AssemblySourcePage>(prefix + "?offset=0&limit=1000"))!;
            var last = (await client.GetFromJsonAsync<AssemblySourcePage>(prefix + "?offset=1000&limit=1000"))!;
            Assert.Equal(1001, first.Total); Assert.Equal(1000, first.Items.Count); Assert.Single(last.Items);
            Assert.Equal(1001, first.Items.Concat(last.Items).Select(item => item.DocumentId).Distinct().Count());
            var repeat = (await client.GetFromJsonAsync<AssemblySourcePage>(prefix + "?offset=0&limit=1000"))!;
            Assert.Equal(first.Items.Select(item => item.DocumentId), repeat.Items.Select(item => item.DocumentId));
            Assert.Empty((await client.GetFromJsonAsync<AssemblySourcePage>(prefix + "?offset=1001&limit=100"))!.Items);
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task MissingUploadRevisionOnRevisionZeroIsRejectedWithoutRecordingASource()
    {
        var rig = await AssemblyRig.CreateAsync();
        var zero = new SubmissionApplication("revisionzero-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", Path.Combine(rig.Root, "revisionzero"), "us-fda-ectd-3.2.2");
        zero.CreateSequence("0001", "original", "Required revision contract"); await rig.Applications.AddAsync(zero);
        await using var host = CreateHost(rig, new AssemblyExecutionOptions(), null, startWorker: false);
        await host.StartAsync();
        try
        {
            using var client = Client(host); using var content = Upload(rig.Original, null);
            using var response = await client.PostAsync(Prefix(zero.Id) + "/assembly-sources", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Single(await rig.Documents.ListAsync());
            Assert.Equal(0, await rig.Revisions.GetRevisionAsync(zero.Id, "0001"));
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task ActualHttpPreviewSubsetWorkerReplayScopeCancelTimeoutAndLegacyRetirement()
    {
        var rig = await AssemblyRig.CreateAsync(); var inspector = new ControlledInspector();
        var options = new AssemblyExecutionOptions { PollInterval = TimeSpan.FromMilliseconds(20), HeartbeatInterval = TimeSpan.FromMilliseconds(25), LeaseDuration = TimeSpan.FromSeconds(5) };
        await using var host = CreateHost(rig, options, inspector, startWorker: true); await host.StartAsync();
        try
        {
            using var client = Client(host); var prefix = Prefix(rig.Application.Id);
            using var form = Upload(rig.Original, rig.Revision); using var uploaded = await client.PostAsync(prefix + "/assembly-sources", form);
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode); var receipt = (await uploaded.Content.ReadFromJsonAsync<AssemblySourceUpload>())!;
            Assert.Equal(rig.Document.Sha256, receipt.Sha256);
            using var invalidPage = await client.GetAsync(prefix + "/assembly-sources?offset=-1&limit=1001"); Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
            var sourcePage = (await client.GetFromJsonAsync<AssemblySourcePage>(prefix + "/assembly-sources?offset=0&limit=1"))!;
            Assert.Equal(1, sourcePage.Total); Assert.Equal(1, sourcePage.Limit); Assert.Equal(receipt.DocumentId, Assert.Single(sourcePage.Items).DocumentId);
            var preview = await PostAsync<AssemblyPreview>(client, prefix + "/assembly-plans", new AssemblyPreviewRequest(rig.Revision,
                $"sourceFile,nodeInstanceId,fileName,operation\n{receipt.SourceFile},{rig.Alpha},http.pdf,new\n,{rig.Beta},missing.pdf,new"));
            Assert.False(preview.CanExecute); Assert.Equal(1, preview.Plan.InvalidRows);
            var page = (await client.GetFromJsonAsync<AssemblyPreviewPage>(prefix + $"/assembly-plans/{preview.Id}/rows?offset=1&limit=1"))!;
            Assert.Equal(2, page.TotalRows); Assert.Equal(1, page.Offset); Assert.Equal(3, Assert.Single(page.Plan.Rows).LineNumber);
            var selected = await PostAsync<AssemblyPreview>(client, prefix + $"/assembly-plans/{preview.Id}/selection", new AssemblySelectionRequest(rig.Revision, preview.Plan.PlanDigest, [2]));
            Assert.True(selected.CanExecute); Assert.NotEqual(preview.Id, selected.Id);
            var loaded = (await client.GetFromJsonAsync<AssemblyPreview>(prefix + $"/assembly-plans/{selected.Id}"))!; Assert.Equal(selected.Plan.PlanDigest, loaded.Plan.PlanDigest);
            var request = new AssemblyEnqueueRequest(selected.Id, selected.Plan.PlanDigest, "http-" + selected.Id);
            var queued = await PostAsync<AssemblyExecutionJobView>(client, prefix + "/assembly-executions", request);
            var result = await WaitAsync(client, prefix, queued.Id, terminal: true);
            Assert.Equal(PdfProcessingJobStatus.Completed, result.Status); Assert.Single(result.Result!.Documents);
            Assert.Equal(rig.Revision + 1, result.Result.WorkspaceRevision);
            Assert.Equal(queued.Id, (await PostAsync<AssemblyExecutionJobView>(client, prefix + "/assembly-executions", request)).Id);
            Assert.Equal(queued.Id, (await client.GetFromJsonAsync<AssemblyExecutionJobView>(prefix + $"/assembly-plans/{selected.Id}/execution"))!.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Prefix(Guid.NewGuid()) + $"/assembly-executions/{queued.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/api/assembly-plans/execute", new { })).StatusCode);

            foreach (var timeout in new[] { false, true })
            {
                inspector.Block = false;
                var plan = await PostAsync<AssemblyPreview>(client, prefix + "/assembly-plans", new AssemblyPreviewRequest(rig.Revision + 1,
                    $"sourceFile,nodeInstanceId,fileName,operation\n{receipt.SourceFile},{rig.Alpha},{(timeout ? "timeout" : "cancel")}.pdf,new"));
                inspector.Block = true;
                if (timeout) options.ExecutionTimeout = TimeSpan.FromMilliseconds(200);
                var waiting = await PostAsync<AssemblyExecutionJobView>(client, prefix + "/assembly-executions", new AssemblyEnqueueRequest(plan.Id, plan.Plan.PlanDigest, Guid.NewGuid().ToString("N")));
                await WaitAsync(client, prefix, waiting.Id, terminal: false);
                if (!timeout) await PostAsync<AssemblyExecutionJobView>(client, prefix + $"/assembly-executions/{waiting.Id}/cancel", new { });
                var stopped = await WaitAsync(client, prefix, waiting.Id, terminal: true);
                Assert.Equal(timeout ? PdfProcessingJobStatus.TimedOut : PdfProcessingJobStatus.Cancelled, stopped.Status); Assert.Null(stopped.Result);
                Assert.Single(await rig.Placements.ListBySequenceAsync(rig.Application.Id, "0001"));
                Assert.Equal(rig.Revision + 1, await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0001"));
            }
            Assert.Equal(rig.Original, await File.ReadAllBytesAsync(rig.Document.StoragePath));
        }
        finally { inspector.Block = false; await host.StopAsync(); }
    }

    internal static WebApplication CreateHost(AssemblyRig rig, AssemblyExecutionOptions options, IPackagePdfInspector? inspector, bool startWorker)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(AssemblyController).Assembly.GetName().Name, EnvironmentName = "Development", ContentRootPath = rig.Root });
        builder.Logging.ClearProviders(); builder.Services.AddSingleton(rig.Store); builder.Services.AddSingleton<IAssemblyStore>(rig.Store);
        builder.Services.AddSingleton(options); builder.Services.AddSingleton(rig.MakeService(rig.Placements, options, inspector));
        builder.Services.RemoveAll<IHostedService>(); if (startWorker) builder.Services.AddHostedService<AssemblyBackgroundService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(AssemblyController).Assembly);
        var host = builder.Build(); host.UseMiddleware<GlobalExceptionMiddleware>(); host.MapControllers(); host.Urls.Add("http://127.0.0.1:0"); return host;
    }
    internal static HttpClient Client(WebApplication host) => new() { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = TimeSpan.FromSeconds(20) };
    internal static string Prefix(Guid app) => $"/api/applications/{app}/sequences/0001";
    private static MultipartFormDataContent Upload(byte[] bytes, long? revision)
    {
        var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", "upload.pdf");
        if (revision is not null) form.Add(new StringContent(revision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedRevision");
        return form;
    }
    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<AssemblyExecutionJobView> WaitAsync(HttpClient client, string prefix, Guid id, bool terminal)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var state = (await client.GetFromJsonAsync<AssemblyExecutionJobView>(prefix + $"/assembly-executions/{id}", deadline.Token))!;
            if (terminal ? state.Status is not (PdfProcessingJobStatus.Pending or PdfProcessingJobStatus.Running) : state.Status != PdfProcessingJobStatus.Pending) return state;
            await Task.Delay(20, deadline.Token);
        }
    }
    private sealed class ControlledInspector : IPackagePdfInspector
    {
        public bool Block { get; set; }
        private readonly PdfPigPackageInspector _inner = new();
        public string EngineVersion => _inner.EngineVersion;
        public PackagePdfFacts Inspect(Stream content, string href, PackageReadLimits limits, CancellationToken cancellationToken = default)
        {
            if (Block) { cancellationToken.WaitHandle.WaitOne(); cancellationToken.ThrowIfCancellationRequested(); }
            return _inner.Inspect(content, href, limits, cancellationToken);
        }
    }
}
