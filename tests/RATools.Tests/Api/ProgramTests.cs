using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RATools.Tests.Api;

public sealed class ProgramTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProgramTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_RedirectsToHealth_WhenSwaggerIsDisabled()
    {
        var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Deployment:Mode"] = "LocalOnly",
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

        var response = await client.GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/health", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task FrontendBuild_IsServedWithSpaFallback_WhenWebRootExists()
    {
        using var webRoot = new TempWebRoot();
        var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseWebRoot(webRoot.Path);
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Deployment:Mode"] = "LocalOnly",
                        ["Swagger:Enabled"] = "true",
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

        var rootResponse = await client.GetAsync("/");
        using var routeRequest = new HttpRequestMessage(HttpMethod.Get, "/applications/app-1");
        routeRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var routeResponse = await client.SendAsync(routeRequest);
        var assetResponse = await client.GetAsync("/assets/app.js");
        var runtimeConfigResponse = await client.GetAsync("/runtime-config");
        var apiResponse = await client.GetAsync("/api/applications");
        var runtimeOpenApi = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var snapshotOpenApi = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "openapi.v1.json")));

        Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
        Assert.Equal("text/html", rootResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("ratools-frontend", await rootResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, routeResponse.StatusCode);
        Assert.Contains("ratools-frontend", await routeResponse.Content.ReadAsStringAsync());
        Assert.Equal("console.log('ratools');", await assetResponse.Content.ReadAsStringAsync());
        var runtimeConfig = await runtimeConfigResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("test-key", runtimeConfig?["apiKey"]);
        Assert.Contains("no-store", runtimeConfigResponse.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
        Assert.True(
            JsonNode.DeepEquals(snapshotOpenApi, runtimeOpenApi),
            $"Serving the frontend must not change the public OpenAPI document. Snapshot root: {snapshotOpenApi?["paths"]?["/"]}; runtime root: {runtimeOpenApi?["paths"]?["/"]}");
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetails()
    {
        var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Deployment:Mode"] = "LocalOnly",
                        ["Swagger:Enabled"] = "false",
                        ["Security:ApiKey"] = "test-key",
                        ["Security:AllowedWorkspaceRoots:0"] = Path.GetTempPath(),
                        ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "ratools-test-uploads"),
                        ["BackboneOutput:RootPath"] = Path.Combine(Path.GetTempPath(), "ratools-test-publish"),
                        ["ValidationProfile:Name"] = "fda-ectd-3.2-manual",
                        ["ValidationProfile:Mode"] = "relaxed"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupFilter, ThrowEndpointStartupFilter>();
                });
                builder.ConfigureLogging(logging => logging.ClearProviders());
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");

        var response = await client.GetAsync("/throw-test-exception");
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal("An error occurred while processing your request.", problem!["title"].ToString());
        Assert.Equal("500", problem["status"].ToString());
        Assert.True(problem.ContainsKey("traceId"));
    }

    private sealed class ThrowEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);
                app.Use(async (context, nextDelegate) =>
                {
                    if (context.Request.Path == "/throw-test-exception")
                    {
                        throw new InvalidOperationException("Test exception");
                    }
                    await nextDelegate();
                });
            };
        }
    }

    private sealed class TempWebRoot : IDisposable
    {
        public TempWebRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ratools-web-root-{Guid.NewGuid():N}");
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "assets"));
            File.WriteAllText(
                System.IO.Path.Combine(Path, "index.html"),
                "<!doctype html><html><body><main>ratools-frontend</main></body></html>");
            File.WriteAllText(
                System.IO.Path.Combine(Path, "assets", "app.js"),
                "console.log('ratools');");
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

    [Fact]
    public async Task ApiApplications_ReturnsUnauthorized_WhenApiKeyIsMissing()
    {
        var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Deployment:Mode"] = "LocalOnly",
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

        var response = await client.GetAsync("/api/applications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOk_WhenApiKeyIsMissing()
    {
        var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Deployment:Mode"] = "LocalOnly",
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

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Version_ReturnsOk_WhenApiKeyIsMissing()
    {
        var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Deployment:Mode"] = "LocalOnly",
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

        var response = await client.GetAsync("/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("RATools.Api", payload?["name"]);
        Assert.StartsWith("0.1.0", payload?["version"]);
    }
}
