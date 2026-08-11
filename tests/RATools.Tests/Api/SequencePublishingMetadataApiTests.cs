using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RATools.Tests.Api;

public sealed class SequencePublishingMetadataApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SequencePublishingMetadataApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublishingMetadataEndpoints_RoundTripTypedMetadata()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "test-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");

        var createResponse = await client.PostAsJsonAsync("/api/applications", new
        {
            ApplicationNumber = "IND-001",
            EctdTemplateKey = "us-fda-ectd-3.2.2",
            SponsorName = "Demo Sponsor",
            WorkingDirectoryParentPath = tempRoot.Path
        });
        var application = await createResponse.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(application);

        var sequenceResponse = await client.PostAsJsonAsync($"/api/applications/{application!.Id}/sequences", new
        {
            SequenceNumber = "0000",
            SubmissionType = "original-application",
            Description = "Initial sequence"
        });
        Assert.Equal(HttpStatusCode.OK, sequenceResponse.StatusCode);

        var defaultResponse = await client.GetAsync($"/api/applications/{application.Id}/sequences/0000/publishing-metadata");
        var defaults = await defaultResponse.Content.ReadFromJsonAsync<PublishingMetadataResponse>();
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        Assert.NotNull(defaults);
        Assert.Equal("Demo Sponsor", defaults!.ApplicantName);
        Assert.Equal("original-application", defaults.SubmissionType);
        Assert.Null(defaults.ApplicantContactName);
        Assert.Null(defaults.ApplicantContactType);
        Assert.Null(defaults.Telephone);
        Assert.Null(defaults.TelephoneNumberType);
        Assert.Null(defaults.Email);

        var updateResponse = await client.PutAsJsonAsync($"/api/applications/{application.Id}/sequences/0000/publishing-metadata", new
        {
            ApplicationType = "IND",
            SubmissionType = "protocol-amendment",
            SubmissionSubtype = "safety",
            SequenceDescription = "Updated sequence description",
            ApplicantName = "Updated Applicant",
            FormType = "form-1571",
            ApplicantContactName = "Jane Regulatory",
            ApplicantContactType = "regulatory",
            Telephone = "301-555-0100",
            TelephoneNumberType = "office",
            Email = "jane.regulatory@example.test"
        });
        var updated = await updateResponse.Content.ReadFromJsonAsync<PublishingMetadataResponse>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.NotNull(updated);
        Assert.Equal("IND", updated!.ApplicationType);
        Assert.Equal("protocol-amendment", updated.SubmissionType);
        Assert.Equal("safety", updated.SubmissionSubtype);
        Assert.Equal("Updated sequence description", updated.SequenceDescription);
        Assert.Equal("Updated Applicant", updated.ApplicantName);
        Assert.Equal("form-1571", updated.FormType);
        Assert.Equal("Jane Regulatory", updated.ApplicantContactName);
        Assert.Equal("regulatory", updated.ApplicantContactType);
        Assert.Equal("301-555-0100", updated.Telephone);
        Assert.Equal("office", updated.TelephoneNumberType);
        Assert.Equal("jane.regulatory@example.test", updated.Email);
    }

    [Fact]
    public async Task GetPublishingMetadata_ReturnsNotFound_WhenSequenceDoesNotExist()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "test-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");

        var response = await client.GetAsync($"/api/applications/{Guid.NewGuid()}/sequences/0000/publishing-metadata");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
                        ["Deployment:Mode"] = "LocalOnly",
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

    private sealed record PublishingMetadataResponse(
        Guid ApplicationId,
        string SequenceNumber,
        string StandardsProfile,
        string? ApplicationType,
        string SubmissionType,
        string? SubmissionSubtype,
        string SequenceDescription,
        string ApplicantName,
        string? FormType,
        string? ApplicantContactName,
        string? ApplicantContactType,
        string? Telephone,
        string? TelephoneNumberType,
        string? Email);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-metadata-api-{Guid.NewGuid():N}");
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
