using System.Net;
using System.Net.Http.Json;
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

        var documentPath = Path.Combine(tempRoot.Path, "ANDA123456", "0001", "m1", "us", "12-cover-letters", "cover.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
        await File.WriteAllTextAsync(documentPath, "cover");

        var documentResponse = await client.PostAsJsonAsync("/api/documents", new
        {
            FileName = "cover.pdf",
            MediaType = "application/pdf",
            FileSize = 5,
            Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            StoragePath = documentPath
        });
        var document = await documentResponse.Content.ReadFromJsonAsync<DocumentResponse>();
        Assert.Equal(HttpStatusCode.Created, documentResponse.StatusCode);
        Assert.NotNull(document);

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
        Assert.Contains(readiness.Findings, x => x.Code == "US_REGIONAL_METADATA_MISSING" && x.FieldName == "ApplicantContactName");
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

    private sealed record ApplicationResponse(Guid Id);
    private sealed record DocumentResponse(Guid Id);
    private sealed record PublishReadinessResponse(bool IsReady, string Status, IReadOnlyCollection<PublishReadinessFindingResponse> Findings);
    private sealed record PublishReadinessFindingResponse(string Code, string? FieldName);

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
