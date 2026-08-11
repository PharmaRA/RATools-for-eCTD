using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Api;

public sealed class AuditLogWriteBoundaryTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PublicPost_IsRejectedWithoutCreatingClientSuppliedAuditEntry()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        var before = await ReadTotalCountAsync(client);

        var response = await client.PostAsJsonAsync("/api/audit-logs", new
        {
            entityType = "PublishJob",
            entityId = Guid.NewGuid().ToString(),
            action = "Completed",
            actor = "forged-client-actor",
            details = "Client supplied audit data must never be persisted.",
        });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(before, await ReadTotalCountAsync(client));
    }

    [Fact]
    public async Task WriteSystemEventAsync_DerivesActorInsideBusinessService()
    {
        var repository = new InMemoryAuditLogRepository();
        var service = new AuditLogService(repository);

        var created = await service.WriteSystemEventAsync(
            new CreateAuditLogRequest("PublishJob", Guid.NewGuid().ToString(), "Completed", "Server event."));

        Assert.Equal("system", created.Actor);
        Assert.Equal("system", Assert.Single(await repository.ListAsync()).Actor);
    }

    private HttpClient CreateClient()
    {
        return factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Persistence:Provider", "InMemory");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Deployment:Mode"] = "LocalOnly",
                        ["Swagger:Enabled"] = "false",
                        ["Security:ApiKey"] = "test-key",
                    });
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static async Task<int> ReadTotalCountAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/audit-logs?page=1&pageSize=20");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("totalCount").GetInt32();
    }
}
