using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RATools.Tests.Api;

public sealed class PublishReadinessApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PublishReadinessApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublishReadinessEndpoint_ReportsBlockedWhenRegionalMetadataIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "test-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");

        var createResponse = await client.PostAsJsonAsync("/api/applications", new
        {
            ApplicationNumber = "ANDA123456",
            EctdTemplateKey = "us-fda-ectd-3.2.2",
            SponsorName = "Acme Pharma",
            WorkingDirectoryParentPath = tempRoot.Path
        });
        var application = await createResponse.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(application);

        var sequenceResponse = await client.PostAsJsonAsync($"/api/applications/{application!.Id}/sequences", new
        {
            SequenceNumber = "0001",
            SubmissionType = "original-application",
            Description = "Initial sequence"
        });
        Assert.Equal(HttpStatusCode.OK, sequenceResponse.StatusCode);

        var document = await UploadSequenceDocumentAsync(client, application.Id);

        var placementResponse = await client.PostAsJsonAsync("/api/document-placements", new
        {
            DocumentId = document!.Id,
            ApplicationId = application.Id,
            SequenceNumber = "0001",
            CtdSection = "m1.2",
            Operation = "new",
            Title = "Cover Letter"
        });
        Assert.Equal(HttpStatusCode.OK, placementResponse.StatusCode);

        var readinessResponse = await client.PostAsJsonAsync("/api/validation/publish-readiness", new
        {
            ApplicationId = application.Id,
            SequenceNumber = "0001"
        });
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<PublishReadinessResponse>();

        Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);
        Assert.NotNull(readiness);
        Assert.False(readiness!.IsReady);
        Assert.Equal("Blocked", readiness.Status);
        Assert.Contains(readiness.CategorySummaries, x =>
            x.Category == "RegionalMetadata"
            && x.BlockingErrorCount == 1
            && x.WarningCount == 0
            && x.FindingCount == 1);
        Assert.Equal(["ApplicantContactName"], readiness.MissingMetadataFields);
        Assert.Contains(readiness.Findings, x =>
            x.Code == "US_REGIONAL_METADATA_MISSING"
            && x.FieldName == "ApplicantContactName"
            && x.Category == "RegionalMetadata"
            && x.RecommendedAction == "Populate the required US Regional publishing metadata field before publishing.");
    }

    [Fact]
    public async Task PublishExecuteEndpoint_StopsWhenReadinessIsBlocked()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "test-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");

        var applicationId = await CreateBlockedPublishScenarioAsync(client, tempRoot.Path);

        var executeResponse = await client.PostAsJsonAsync("/api/publish-jobs/execute", new
        {
            ApplicationId = applicationId,
            SequenceNumber = "0001"
        });

        // 发布改为后台执行：/execute 返回 202 与作业 id，结果通过轮询作业获取。
        Assert.Equal(HttpStatusCode.Accepted, executeResponse.StatusCode);
        var acceptedJob = await executeResponse.Content.ReadFromJsonAsync<PublishJobResponse>();
        Assert.NotNull(acceptedJob);

        var job = await PollUntilTerminalAsync(client, acceptedJob!.Id);
        Assert.Equal("Failed", job.Status);
        Assert.Null(job.OutputPath);
        Assert.Null(job.PackagePath);
    }

    private static async Task<PublishJobResponse> PollUntilTerminalAsync(HttpClient client, Guid jobId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var job = await client.GetFromJsonAsync<PublishJobResponse>($"/api/publish-jobs/{jobId}");
            if (job is not null && job.Status is "Completed" or "Failed")
            {
                return job;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException($"Publish job {jobId} did not reach a terminal status in time.");
    }

    private HttpClient CreateClient(string allowedRoot, string apiKey)
    {
        return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Persistence:Provider", "InMemory");
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.Sources.Clear();
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Swagger:Enabled"] = "false",
                        ["Security:ApiKey"] = apiKey,
                        ["Security:AllowedWorkspaceRoots:0"] = allowedRoot,
                        ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "ratools-test-uploads"),
                        ["BackboneOutput:RootPath"] = Path.Combine(Path.GetTempPath(), "ratools-test-publish"),
                        ["ValidationProfile:Name"] = "fda-ectd-3.2-manual",
                        ["ValidationProfile:Mode"] = "relaxed"
                    });
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
    }

    private static async Task<Guid> CreateBlockedPublishScenarioAsync(HttpClient client, string root)
    {
        var createResponse = await client.PostAsJsonAsync("/api/applications", new
        {
            ApplicationNumber = "ANDA123456",
            EctdTemplateKey = "us-fda-ectd-3.2.2",
            SponsorName = "Acme Pharma",
            WorkingDirectoryParentPath = root
        });
        var application = await createResponse.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(application);

        var sequenceResponse = await client.PostAsJsonAsync($"/api/applications/{application!.Id}/sequences", new
        {
            SequenceNumber = "0001",
            SubmissionType = "original-application",
            Description = "Initial sequence"
        });
        Assert.Equal(HttpStatusCode.OK, sequenceResponse.StatusCode);

        var document = await UploadSequenceDocumentAsync(client, application.Id);

        var placementResponse = await client.PostAsJsonAsync("/api/document-placements", new
        {
            DocumentId = document!.Id,
            ApplicationId = application.Id,
            SequenceNumber = "0001",
            CtdSection = "m1.2",
            Operation = "new",
            Title = "Cover Letter"
        });
        Assert.Equal(HttpStatusCode.OK, placementResponse.StatusCode);

        return application.Id;
    }

    private static async Task<DocumentResponse> UploadSequenceDocumentAsync(HttpClient client, Guid applicationId)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes("cover"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "File", "cover.pdf");
        form.Add(new StringContent("m1.2"), "CtdSection");

        var response = await client.PostAsync(
            $"/api/applications/{applicationId}/sequences/0001/documents/upload",
            form);
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(document);
        return document!;
    }

    private sealed record ApplicationResponse(Guid Id);
    private sealed record DocumentResponse(Guid Id);
    private sealed record PublishReadinessResponse(
        bool IsReady,
        string Status,
        IReadOnlyCollection<string> MissingMetadataFields,
        IReadOnlyCollection<PublishReadinessCategorySummaryResponse> CategorySummaries,
        IReadOnlyCollection<PublishReadinessFindingResponse> Findings);
    private sealed record PublishReadinessCategorySummaryResponse(string Category, int BlockingErrorCount, int WarningCount, int FindingCount);
    private sealed record PublishReadinessFindingResponse(string Code, string? FieldName, string Category, string RecommendedAction);
    private sealed record PublishJobResponse(Guid Id, string Status, string? OutputPath, string? PackagePath);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-readiness-api-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
