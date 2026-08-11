using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RATools.Tests.Api;

public sealed class OpenApiContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task RuntimeDocument_MatchesCommittedSnapshot()
    {
        var client = factory.WithWebHostBuilder(builder =>
            {
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
            .CreateClient();

        var runtimeJson = await client.GetStringAsync("/swagger/v1/swagger.json");
        var snapshotJson = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "openapi.v1.json"));

        var runtimeDocument = JsonNode.Parse(runtimeJson);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(snapshotJson), runtimeDocument),
            "The runtime OpenAPI document differs from openapi.v1.json. Run `cd frontend && npm run api:generate`.");

        var schemas = runtimeDocument!["components"]!["schemas"]!;
        var auditLogRequired = schemas["AuditLogDto"]!["required"]!.AsArray();
        Assert.Contains(auditLogRequired, value => value!.GetValue<string>() == "details");
        Assert.True(schemas["AuditLogDto"]!["properties"]!["details"]!["nullable"]!.GetValue<bool>());

        var placementRequestRequired = schemas["CreateDocumentPlacementRequestBody"]!["required"]!.AsArray();
        Assert.DoesNotContain(placementRequestRequired, value => value!.GetValue<string>() == "title");
    }
}
