using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
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

public sealed class ProcessingHttpTests
{
    [Fact]
    public async Task ActualHttpWorkerRangePreviewAndExplicitAdoptionPreserveTheSource()
    {
        var root = Path.GetFullPath($".artifacts/p5-06-http-tests/{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "0001", "m3"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(DocumentProcessingController).Assembly.GetName().Name, EnvironmentName = "Development", ContentRootPath = root });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Configuration["Persistence:Provider"] = "InMemory";
        builder.Configuration["PdfProcessing:Engine:ExecutablePath"] = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        builder.Configuration["PdfProcessing:PollInterval"] = "00:00:00.050";
        builder.Configuration["PdfProcessing:HeartbeatInterval"] = "00:00:00.100";
        builder.Configuration["PdfProcessing:LeaseDuration"] = "00:00:10";
        builder.Logging.ClearProviders();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        var controlledEngine = new ControlledEngine(new QpdfProcessingEngine(new QpdfProcessingOptions
        { ExecutablePath = builder.Configuration["PdfProcessing:Engine:ExecutablePath"]! }));
        builder.Services.AddSingleton<IPdfProcessingEngine>(controlledEngine);
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.AddHostedService<DocumentProcessingBackgroundService>();
        builder.Services.AddSingleton<IWorkspacePathPolicy>(new TestPaths(root));
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentProcessingController).Assembly);
        await using var host = builder.Build();
        host.UseMiddleware<GlobalExceptionMiddleware>();
        host.MapControllers();
        host.Urls.Add("http://127.0.0.1:0");
        var application = new SubmissionApplication(Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "HTTP processing");
        var documentRepository = host.Services.GetRequiredService<IDocumentRepository>();
        var placementRepository = host.Services.GetRequiredService<IDocumentPlacementRepository>();
        await host.Services.GetRequiredService<IApplicationRepository>().AddAsync(application);
        var bytes = await File.ReadAllBytesAsync("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        SubmissionDocument? source = null; DocumentPlacement? placement = null;
        foreach (var name in new[] { "source.pdf", "old.pdf" })
        {
            var path = Path.Combine(root, "0001", "m3", name);
            await File.WriteAllBytesAsync(path, bytes);
            var document = new SubmissionDocument(name, "application/pdf", bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path);
            await documentRepository.AddAsync(document);
            var leaf = new DocumentPlacement(document.Id, application.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, name);
            await placementRepository.AddAsync(leaf);
            if (name == "source.pdf") { source = document; placement = leaf; }
        }
        await host.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = TimeSpan.FromSeconds(30) };
            var prefix = $"/api/applications/{application.Id}/sequences/0001/pdf-processing";
            var inspect = await PostAsync<ProcessingInspection>(client, prefix + $"/placements/{placement!.Id}/inspection", new { expectedRevision = 0 });
            var plan = await PostAsync<PdfProcessingPlan>(client, prefix + $"/placements/{placement.Id}/plans",
                new CreateProcessingPlanRequest(inspect.SourceVersionId, 0, new("UseOutlines", [new("HTTP处理", 2, null, 0)], null)));
            var queuedResponse = await client.PostAsJsonAsync(prefix + "/jobs", new { planId = plan.Id, planDigest = plan.PlanDigest, idempotencyKey = "http-test" });
            Assert.Equal(HttpStatusCode.Accepted, queuedResponse.StatusCode);
            var queued = (await queuedResponse.Content.ReadFromJsonAsync<ProcessingJobView>())!;
            ProcessingJobView state = queued;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (state.Status is PdfProcessingJobStatus.Pending or PdfProcessingJobStatus.Running)
            {
                await Task.Delay(50, deadline.Token);
                state = (await client.GetFromJsonAsync<ProcessingJobView>(prefix + $"/jobs/{queued.Id}", deadline.Token))!;
            }
            Assert.Equal(PdfProcessingJobStatus.Completed, state.Status);
            Assert.Null(state.Adoption);
            Assert.Equal(source!.Id, (await placementRepository.GetAsync(placement.Id))!.DocumentId);
            using var range = new HttpRequestMessage(HttpMethod.Get, prefix + $"/versions/{state.ResultVersionId}/content");
            range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 15);
            var content = await client.SendAsync(range);
            Assert.Equal(HttpStatusCode.PartialContent, content.StatusCode);
            Assert.Equal(16, (await content.Content.ReadAsByteArrayAsync()).Length);
            Assert.Equal('"' + state.ResultSha256 + '"', content.Headers.ETag!.Tag);
            var foreign = await client.GetAsync($"/api/applications/{Guid.NewGuid()}/sequences/0001/pdf-processing/versions/{state.ResultVersionId}/content");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            var adoption = await PostAsync<PdfProcessingAdoption>(client, prefix + $"/jobs/{queued.Id}/adopt", new { expectedRevision = 0 });
            Assert.Equal(1, adoption.WorkspaceRevision);
            var replay = await PostAsync<PdfProcessingAdoption>(client, prefix + $"/jobs/{queued.Id}/adopt", new { expectedRevision = 0 });
            Assert.Equal(adoption.DocumentId, replay.DocumentId);
            Assert.Equal(placement.LeafId, (await placementRepository.GetAsync(placement.Id))!.LeafId);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(source.StoragePath));

            foreach (var timeout in new[] { false, true })
            {
                var current = await PostAsync<ProcessingInspection>(client, prefix + $"/placements/{placement.Id}/inspection", new { expectedRevision = 1 });
                var next = await PostAsync<PdfProcessingPlan>(client, prefix + $"/placements/{placement.Id}/plans",
                    new CreateProcessingPlanRequest(current.SourceVersionId, 1, new("UseNone", null, null)));
                controlledEngine.Block = true;
                if (timeout) host.Services.GetRequiredService<PdfProcessingExecutionOptions>().ExecutionTimeout = TimeSpan.FromMilliseconds(300);
                var waiting = await PostAsync<ProcessingJobView>(client, prefix + "/jobs",
                    new { planId = next.Id, planDigest = next.PlanDigest, idempotencyKey = "interrupt-" + timeout });
                using var waitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (waiting.Status == PdfProcessingJobStatus.Pending)
                {
                    await Task.Delay(30, waitDeadline.Token);
                    waiting = (await client.GetFromJsonAsync<ProcessingJobView>(prefix + $"/jobs/{waiting.Id}", waitDeadline.Token))!;
                }
                if (!timeout) await PostAsync<ProcessingJobView>(client, prefix + $"/jobs/{waiting.Id}/cancel", new { });
                while (waiting.Status == PdfProcessingJobStatus.Running)
                {
                    await Task.Delay(30, waitDeadline.Token);
                    waiting = (await client.GetFromJsonAsync<ProcessingJobView>(prefix + $"/jobs/{waiting.Id}", waitDeadline.Token))!;
                }
                Assert.Equal(timeout ? PdfProcessingJobStatus.TimedOut : PdfProcessingJobStatus.Cancelled, waiting.Status);
                Assert.Null(waiting.ResultVersionId);
                Assert.Equal(adoption.DocumentId, (await placementRepository.GetAsync(placement.Id))!.DocumentId);
                using var rejected = await client.PostAsJsonAsync(prefix + $"/jobs/{waiting.Id}/adopt", new { expectedRevision = 1 });
                Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            }
        }
        finally { await host.StopAsync(); }
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed class TestPaths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        {
            var full = Path.GetFullPath(path);
            if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Outside test workspace.");
            return full;
        }
    }

    private sealed class ControlledEngine(IPdfProcessingEngine inner) : IPdfProcessingEngine
    {
        public bool Block { get; set; }
        public string EngineVersion => inner.EngineVersion;
        public Task<PdfNavigationInspection> InspectAsync(string path, long maximum, CancellationToken cancellationToken = default) =>
            inner.InspectAsync(path, maximum, cancellationToken);
        public async Task<PdfEngineResult> ExecuteAsync(PdfEngineRequest request, CancellationToken cancellationToken = default)
        {
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            return await inner.ExecuteAsync(request, cancellationToken);
        }
    }
}
