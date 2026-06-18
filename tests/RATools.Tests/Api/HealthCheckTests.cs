using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RATools.Tests.Api;

public sealed class HealthCheckTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    [Fact]
    public async Task LiveProbe_ReturnsOk_WhenProcessIsRunning()
    {
        var client = CreateInMemoryClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyProbe_ReturnsOk_WhenNoDependencyChecksRegistered()
    {
        // InMemory provider registers no database probe, so readiness has no
        // dependency checks and reports healthy.
        var client = CreateInMemoryClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LegacyHealth_StillReturnsOkContract()
    {
        var client = CreateInMemoryClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthEndpoints_AllowAnonymousAccess()
    {
        var client = CreateInMemoryClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.NotEqual(HttpStatusCode.Unauthorized, live.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, ready.StatusCode);
    }

    private HttpClient CreateInMemoryClient()
    {
        return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Swagger:Enabled"] = "false",
                        ["Security:ApiKey"] = "test-key",
                        ["Security:AllowedWorkspaceRoots:0"] = Path.GetTempPath(),
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
}
