using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RATools.Api.Controllers;
using RATools.Api.Middleware;
using RATools.Application;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.DocumentProcessing;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure;
using RATools.Infrastructure.DocumentProcessing;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfRenameHttpTests
{
    [Fact]
    public async Task HttpBatchWorkerContentRecoveryAndAdoptionThenCancellationAndTimeout()
    {
        var root = Path.GetFullPath(".artifacts/p507-http/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "0001", "m5"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(PdfRenameController).Assembly.GetName().Name,
            EnvironmentName = "Development", ContentRootPath = root });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection();
        builder.Configuration["Persistence:Provider"] = "InMemory";
        builder.Configuration["PdfProcessing:Engine:ExecutablePath"] = Environment.GetEnvironmentVariable("RATOOLS_QPDF") ??
            Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        builder.Configuration["PdfProcessing:PollInterval"] = "00:00:00.050";
        builder.Configuration["PdfProcessing:HeartbeatInterval"] = "00:00:00.100";
        builder.Configuration["PdfProcessing:LeaseDuration"] = "00:00:10";
        builder.Logging.ClearProviders(); builder.Services.AddApplication(); builder.Services.AddInfrastructure(builder.Configuration);
        var engine = new ControlledEngine(new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = builder.Configuration["PdfProcessing:Engine:ExecutablePath"]! }));
        builder.Services.AddSingleton<IPdfProcessingEngine>(engine);
        builder.Services.RemoveAll<IHostedService>(); builder.Services.AddHostedService<PdfRenameBackgroundService>();
        builder.Services.AddSingleton<IWorkspacePathPolicy>(new Paths(root));
        builder.Services.AddControllers().AddApplicationPart(typeof(PdfRenameController).Assembly);
        await using var host = builder.Build(); host.UseMiddleware<GlobalExceptionMiddleware>(); host.MapControllers(); host.Urls.Add("http://127.0.0.1:0");
        var application = new SubmissionApplication("p507-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "HTTP batch");
        await host.Services.GetRequiredService<IApplicationRepository>().AddAsync(application);
        var documents = host.Services.GetRequiredService<IDocumentRepository>(); var placements = host.Services.GetRequiredService<IDocumentPlacementRepository>();
        var mounts = new List<DocumentPlacement>(); var bytes = await File.ReadAllBytesAsync("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        foreach (var name in new[] { "source.pdf", "old.pdf" })
        {
            var path = Path.Combine(root, "0001", "m5", name); await File.WriteAllBytesAsync(path, bytes);
            var document = new SubmissionDocument(name, "application/pdf", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path); await documents.AddAsync(document);
            var placement = new DocumentPlacement(document.Id, application.Id, "0001", "m5.3.1", DocumentPlacementOperation.New, name);
            await placements.AddAsync(placement); mounts.Add(placement);
        }
        await host.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            var prefix = $"/api/applications/{application.Id}/sequences/0001/pdf-renames";
            var request = new PdfRenamePreviewRequest(0, [new(mounts[1].Id, "renamed.pdf")], mounts.Select(item => item.Id).ToArray());
            var plan = await PostAsync<PdfRenamePreview>(client, prefix + "/plans", request);
            Assert.True(plan.Plan.CanExecute);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(prefix + $"/plans/{plan.Id}/job")).StatusCode);
            var submit = new EnqueuePdfRenameRequest(plan.Id, plan.Plan.PlanDigest, "http-" + plan.Id);
            var job = await PostAsync<PdfRenameJobView>(client, prefix + "/jobs", submit);
            job = await WaitAsync(client, prefix, job.Id, terminal: true);
            Assert.Equal(PdfProcessingJobStatus.Completed, job.Status); Assert.Equal(2, job.Results.Count); Assert.Null(job.Adoption);
            Assert.Equal(job.Id, (await client.GetFromJsonAsync<PdfRenameJobView>(prefix + $"/plans/{plan.Id}/job"))!.Id);
            using var range = new HttpRequestMessage(HttpMethod.Get, prefix + $"/jobs/{job.Id}/placements/{mounts[0].Id}/content");
            range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 15);
            using var content = await client.SendAsync(range); Assert.Equal(HttpStatusCode.PartialContent, content.StatusCode);
            Assert.Equal(16, (await content.Content.ReadAsByteArrayAsync()).Length);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/applications/{Guid.NewGuid()}/sequences/0001/pdf-renames/jobs/{job.Id}")).StatusCode);
            var adopted = await PostAsync<PdfRenameAdoption>(client, prefix + $"/jobs/{job.Id}/adopt", new ProcessingRevisionRequest(0));
            Assert.Equal(1, adopted.WorkspaceRevision); Assert.Equal(2, adopted.Documents.Count);
            Assert.Equal(job.Id, (await PostAsync<PdfRenameJobView>(client, prefix + "/jobs", submit)).Id);
            Assert.Equal(adopted.Documents.Select(item => item.DocumentId),
                (await PostAsync<PdfRenameAdoption>(client, prefix + $"/jobs/{job.Id}/adopt", new ProcessingRevisionRequest(0))).Documents.Select(item => item.DocumentId));
            foreach (var original in mounts) Assert.Equal(bytes, await File.ReadAllBytesAsync((await documents.GetAsync(original.DocumentId))!.StoragePath));
            foreach (var timeout in new[] { false, true })
            {
                var next = await PostAsync<PdfRenamePreview>(client, prefix + "/plans", new PdfRenamePreviewRequest(1,
                    [new(mounts[1].Id, timeout ? "timeout.pdf" : "cancel.pdf")], mounts.Select(item => item.Id).ToArray()));
                engine.Block = true;
                if (timeout) host.Services.GetRequiredService<PdfProcessingExecutionOptions>().ExecutionTimeout = TimeSpan.FromMilliseconds(300);
                var waiting = await PostAsync<PdfRenameJobView>(client, prefix + "/jobs", new EnqueuePdfRenameRequest(next.Id, next.Plan.PlanDigest, "interrupt-" + next.Id));
                waiting = await WaitAsync(client, prefix, waiting.Id, terminal: false);
                if (!timeout) await PostAsync<PdfRenameJobView>(client, prefix + $"/jobs/{waiting.Id}/cancel", new { });
                waiting = await WaitAsync(client, prefix, waiting.Id, terminal: true);
                Assert.Equal(timeout ? PdfProcessingJobStatus.TimedOut : PdfProcessingJobStatus.Cancelled, waiting.Status);
                Assert.Empty(waiting.Results);
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(prefix + $"/jobs/{waiting.Id}/adopt", new ProcessingRevisionRequest(1))).StatusCode);
                foreach (var item in adopted.Documents) Assert.Equal(item.DocumentId, (await placements.GetAsync(item.PlacementId))!.DocumentId);
            }
        }
        finally { await host.StopAsync(); }
    }

    private static async Task<PdfRenameJobView> WaitAsync(HttpClient client, string prefix, Guid id, bool terminal)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var state = (await client.GetFromJsonAsync<PdfRenameJobView>(prefix + $"/jobs/{id}", deadline.Token))!;
            if (terminal ? state.Status is not (PdfProcessingJobStatus.Pending or PdfProcessingJobStatus.Running) : state.Status != PdfProcessingJobStatus.Pending) return state;
            await Task.Delay(40, deadline.Token);
        }
    }
    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private sealed class ControlledEngine(IPdfProcessingEngine inner) : IPdfProcessingEngine
    {
        public bool Block { get; set; }
        public string EngineVersion => inner.EngineVersion;
        public Task<PdfNavigationInspection> InspectAsync(string path, long maximum, CancellationToken cancellationToken = default) => inner.InspectAsync(path, maximum, cancellationToken);
        public async Task<PdfEngineResult> ExecuteAsync(PdfEngineRequest request, CancellationToken cancellationToken = default)
        {
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            return await inner.ExecuteAsync(request, cancellationToken);
        }
    }
    private sealed class Paths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        {
            var full = Path.GetFullPath(path);
            if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Outside test workspace.");
            return full;
        }
    }
}
