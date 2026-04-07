using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace RATools.Api.Tests.Frontend;

public sealed class StaticPagesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StaticPagesTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Swagger:Enabled"] = "false"
                });
            });
        });
    }

    [Fact]
    public async Task Root_ReturnsFrontendIndexPage()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("RATools eCTD Admin", body);
    }

    [Fact]
    public async Task ApplicationPage_IsServedAsStaticHtml()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/app.html?applicationId=test");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Publish History", body);
    }
}
