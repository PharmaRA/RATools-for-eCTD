using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RATools.Tests.Api;

public sealed class ApplicationNumberSecurityApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApplicationNumberSecurityApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public static TheoryData<string> UnsafeApplicationNumbers => new()
    {
        "../escape",
        "..\\escape",
        "/var/tmp/escape",
        "C:\\escape",
        "\\\\server\\share",
        "mixed/..\\escape",
        "CON",
        "NUL.json",
        "COM1",
        "LPT9.log"
    };

    public static TheoryData<string> UnsafeSequenceNumbers => new()
    {
        "../0001",
        "..\\0001",
        "/var/tmp/0001",
        "C:\\0001",
        "\\\\server\\share",
        "mixed/..\\0001",
        "CON",
        "NUL.txt"
    };

    [Theory]
    [Trait("Category", "PathSecurity")]
    [MemberData(nameof(UnsafeApplicationNumbers))]
    public async Task CreateApplication_RejectsUnsafeApplicationNumberWithoutChangingFilesOrRepository(
        string applicationNumber)
    {
        using var tempRoot = new TemporaryDirectory();
        var sentinelPath = Path.Combine(tempRoot.Path, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "unchanged");
        var beforeEntries = CaptureEntries(tempRoot.Path);
        var beforeWriteTime = File.GetLastWriteTimeUtc(sentinelPath);
        using var client = CreateClient(tempRoot.Path);

        var response = await client.PostAsJsonAsync("/api/applications", new
        {
            ApplicationNumber = applicationNumber,
            EctdTemplateKey = "us-fda-ectd-3.2.2",
            SponsorName = "Security Test Sponsor",
            WorkingDirectoryParentPath = tempRoot.Path
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(beforeEntries, CaptureEntries(tempRoot.Path));
        Assert.Equal("unchanged", await File.ReadAllTextAsync(sentinelPath));
        Assert.Equal(beforeWriteTime, File.GetLastWriteTimeUtc(sentinelPath));

        var applications = await client.GetFromJsonAsync<ApplicationResponse[]>("/api/applications");
        Assert.NotNull(applications);
        Assert.Empty(applications);
    }

    [Theory]
    [Trait("Category", "PathSecurity")]
    [MemberData(nameof(UnsafeSequenceNumbers))]
    public async Task CreateSequence_RejectsUnsafeSequenceNumberWithoutChangingFilesOrRepository(
        string sequenceNumber)
    {
        using var tempRoot = new TemporaryDirectory();
        var sentinelPath = Path.Combine(tempRoot.Path, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "unchanged");
        using var client = CreateClient(tempRoot.Path);
        var createResponse = await client.PostAsJsonAsync("/api/applications", new
        {
            ApplicationNumber = "app-001",
            EctdTemplateKey = "us-fda-ectd-3.2.2",
            SponsorName = "Security Test Sponsor",
            WorkingDirectoryParentPath = tempRoot.Path
        });
        var application = await createResponse.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(application);
        var beforeEntries = CaptureEntries(tempRoot.Path);
        var beforeWriteTime = File.GetLastWriteTimeUtc(sentinelPath);

        var response = await client.PostAsJsonAsync($"/api/applications/{application!.Id}/sequences", new
        {
            SequenceNumber = sequenceNumber,
            SubmissionType = "original-application",
            Description = "Initial"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(beforeEntries, CaptureEntries(tempRoot.Path));
        Assert.Equal("unchanged", await File.ReadAllTextAsync(sentinelPath));
        Assert.Equal(beforeWriteTime, File.GetLastWriteTimeUtc(sentinelPath));

        var persisted = await client.GetFromJsonAsync<ApplicationResponse>($"/api/applications/{application.Id}");
        Assert.NotNull(persisted);
        Assert.Empty(persisted!.Sequences);
    }

    private HttpClient CreateClient(string allowedRoot)
    {
        var client = _factory.WithWebHostBuilder(builder =>
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
                        ["Security:ApiKey"] = "test-key",
                        ["Security:AllowedWorkspaceRoots:0"] = allowedRoot,
                        ["FileStorage:RootPath"] = allowedRoot,
                        ["BackboneOutput:RootPath"] = allowedRoot
                    });
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        return client;
    }

    private static string[] CaptureEntries(string root)
        => Directory
            .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private sealed record ApplicationResponse(Guid Id, SequenceResponse[] Sequences);

    private sealed record SequenceResponse(string SequenceNumber);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ratools-application-number-api-{Guid.NewGuid():N}");
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
