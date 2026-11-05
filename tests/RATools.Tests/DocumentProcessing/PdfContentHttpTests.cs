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

public sealed class PdfContentHttpTests
{
    [Fact]
    public async Task ActualContentHttpWorkerEvidenceRangeScopeAdoptionCancellationAndTimeout()
    {
        var root = Path.GetFullPath(".artifacts/p508-http/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "0001", "m3"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(DocumentProcessingController).Assembly.GetName().Name, EnvironmentName = "Development", ContentRootPath = root });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection();
        builder.Configuration["Persistence:Provider"] = "InMemory";
        builder.Configuration["PdfProcessing:Engine:ExecutablePath"] = PdfContentTestRig.Qpdf;
        builder.Configuration["PdfProcessing:PollInterval"] = "00:00:00.050";
        builder.Configuration["PdfProcessing:HeartbeatInterval"] = "00:00:00.100";
        builder.Configuration["PdfProcessing:LeaseDuration"] = "00:00:20";
        builder.Logging.ClearProviders(); builder.Services.AddApplication(); builder.Services.AddInfrastructure(builder.Configuration);
        var options = PdfContentTestRig.OptionsFor(root);
        var content = new ControlledContent(new PythonPdfContentEngine(options, new() { ExecutablePath = PdfContentTestRig.Qpdf }));
        builder.Services.AddSingleton<IPdfContentEngine>(content);
        builder.Services.RemoveAll<IHostedService>(); builder.Services.AddHostedService<DocumentProcessingBackgroundService>();
        builder.Services.AddSingleton<IWorkspacePathPolicy>(new PdfContentTestRig.Paths(root));
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentProcessingController).Assembly);
        await using var host = builder.Build(); host.UseMiddleware<GlobalExceptionMiddleware>(); host.MapControllers(); host.Urls.Add("http://127.0.0.1:0");
        var application = new SubmissionApplication("p508http-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "Content HTTP");
        await host.Services.GetRequiredService<IApplicationRepository>().AddAsync(application);
        var path = Path.Combine(root, "0001", "m3", "source.pdf"); await PdfContentTestRig.CreateFixtureAsync("version", path);
        var bytes = await File.ReadAllBytesAsync(path);
        var document = new SubmissionDocument("source.pdf", "application/pdf", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path);
        await host.Services.GetRequiredService<IDocumentRepository>().AddAsync(document);
        var placement = new DocumentPlacement(document.Id, application.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, "HTTP source");
        var placements = host.Services.GetRequiredService<IDocumentPlacementRepository>(); await placements.AddAsync(placement);
        await host.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = TimeSpan.FromSeconds(30) };
            var prefix = $"/api/applications/{application.Id}/sequences/0001/pdf-processing";
            var source = await PostAsync<ProcessingInspection>(client, prefix + $"/placements/{placement.Id}/inspection", new { expectedRevision = 0 });
            options.Enabled = false;
            var disabled = await PostAsync<PdfContentInspection>(client, prefix + $"/placements/{placement.Id}/content-inspection", new PdfContentInspectionRequest(source.SourceVersionId, 0));
            Assert.False(disabled.Capabilities.Enabled); Assert.Contains("CONTENT_NOT_CONFIGURED", disabled.BlockReasons);
            Assert.Equal(source.SourceVersionId, disabled.SourceVersionId); Assert.Equal(source.Sha256, disabled.Sha256);
            options.Enabled = true;
            Assert.True((await client.GetFromJsonAsync<PdfContentCapabilities>(prefix + "/capabilities"))!.Enabled);
            var inspection = await PostAsync<PdfContentInspection>(client, prefix + $"/placements/{placement.Id}/content-inspection", new PdfContentInspectionRequest(source.SourceVersionId, 0));
            Assert.Equal(4, inspection.PageCount);
            var plan = await PostAsync<PdfProcessingPlan>(client, prefix + $"/placements/{placement.Id}/plans",
                new CreateProcessingPlanRequest(source.SourceVersionId, 0, new(null, null, null, Content: PdfContentTestRig.Parameters("version"))));
            var request = new { planId = plan.Id, planDigest = plan.PlanDigest, idempotencyKey = "content-http" };
            var queued = await PostAsync<ProcessingJobView>(client, prefix + "/jobs", request);
            var completed = await WaitAsync(client, prefix, queued.Id, terminal: true);
            Assert.Equal(PdfProcessingJobStatus.Completed, completed.Status);
            Assert.Null(completed.Adoption); Assert.Equal(document.Id, (await placements.GetAsync(placement.Id))!.DocumentId);
            var evidence = (await client.GetFromJsonAsync<PdfContentEvidence>(prefix + $"/jobs/{queued.Id}/content-receipt"))!;
            Assert.Equal(source.Sha256, evidence.Receipt.SourceSha256); Assert.Equal(completed.ResultSha256, evidence.Receipt.OutputSha256);
            using var range = new HttpRequestMessage(HttpMethod.Get, prefix + $"/versions/{completed.ResultVersionId}/content");
            range.Headers.Range = new(0, 15); using var response = await client.SendAsync(range);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal(16, (await response.Content.ReadAsByteArrayAsync()).Length);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/applications/{Guid.NewGuid()}/sequences/0001/pdf-processing/jobs/{queued.Id}/content-receipt")).StatusCode);
            Assert.Equal(queued.Id, (await PostAsync<ProcessingJobView>(client, prefix + "/jobs", request)).Id);
            var adopted = await PostAsync<PdfProcessingAdoption>(client, prefix + $"/jobs/{queued.Id}/adopt", new { expectedRevision = 0 });
            Assert.Equal(1, adopted.WorkspaceRevision);
            Assert.Equal(adopted.DocumentId, (await PostAsync<PdfProcessingAdoption>(client, prefix + $"/jobs/{queued.Id}/adopt", new { expectedRevision = 0 })).DocumentId);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            foreach (var timeout in new[] { false, true })
            {
                var current = await PostAsync<ProcessingInspection>(client, prefix + $"/placements/{placement.Id}/inspection", new { expectedRevision = 1 });
                var next = await PostAsync<PdfProcessingPlan>(client, prefix + $"/placements/{placement.Id}/plans",
                    new CreateProcessingPlanRequest(current.SourceVersionId, 1, new(null, null, null, Content: PdfContentTestRig.Parameters("version"))));
                content.Block = true;
                if (timeout) host.Services.GetRequiredService<PdfProcessingExecutionOptions>().ExecutionTimeout = TimeSpan.FromMilliseconds(350);
                var waiting = await PostAsync<ProcessingJobView>(client, prefix + "/jobs", new { planId = next.Id, planDigest = next.PlanDigest, idempotencyKey = "interrupt-" + timeout });
                waiting = await WaitAsync(client, prefix, waiting.Id, terminal: false);
                if (!timeout) await PostAsync<ProcessingJobView>(client, prefix + $"/jobs/{waiting.Id}/cancel", new { });
                waiting = await WaitAsync(client, prefix, waiting.Id, terminal: true);
                Assert.Equal(timeout ? PdfProcessingJobStatus.TimedOut : PdfProcessingJobStatus.Cancelled, waiting.Status);
                Assert.Null(waiting.ResultVersionId); Assert.Equal(adopted.DocumentId, (await placements.GetAsync(placement.Id))!.DocumentId);
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(prefix + $"/jobs/{waiting.Id}/adopt", new { expectedRevision = 1 })).StatusCode);
            }
        }
        finally { await host.StopAsync(); }
    }
    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<ProcessingJobView> WaitAsync(HttpClient client, string prefix, Guid id, bool terminal)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var state = (await client.GetFromJsonAsync<ProcessingJobView>(prefix + $"/jobs/{id}", timeout.Token))!;
            if (terminal ? state.Status is not (PdfProcessingJobStatus.Pending or PdfProcessingJobStatus.Running) : state.Status != PdfProcessingJobStatus.Pending) return state;
            await Task.Delay(30, timeout.Token);
        }
    }
    private sealed class ControlledContent(IPdfContentEngine inner) : IPdfContentEngine, IDisposable
    {
        public bool Block { get; set; }
        public Task<PdfContentCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => inner.GetCapabilitiesAsync(cancellationToken);
        public Task<PdfContentInspection> InspectAsync(DocumentVersion source, CancellationToken cancellationToken = default) => inner.InspectAsync(source, cancellationToken);
        public Task VerifyEvidenceAsync(DocumentVersion source, string output, PdfContentParameters parameters, PdfContentInspection inspection, PdfContentEvidence evidence, CancellationToken cancellationToken = default) => inner.VerifyEvidenceAsync(source, output, parameters, inspection, evidence, cancellationToken);
        public async Task<PdfContentEvidence> ExecuteAsync(DocumentVersion source, string output, PdfContentParameters parameters, PdfContentInspection inspection, TimeSpan timeout, long maximum, CancellationToken cancellationToken = default)
        { if (Block) await Task.Delay(Timeout.Infinite, cancellationToken); return await inner.ExecuteAsync(source, output, parameters, inspection, timeout, maximum, cancellationToken); }
        public void Dispose() => (inner as IDisposable)?.Dispose();
    }
}
