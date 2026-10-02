using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.Documents.Dtos;
using RATools.Tests.Workspaces;

namespace RATools.Tests.Api;

public sealed class NodePlacementMoveApiTests
{
    [Fact]
    public async Task PreviewAndExistingSectionRouteEnforceRevisionAndReturnNodeIdentity()
    {
        using var workspace = new NodeMoveTestWorkspace();
        await workspace.SeedAsync();
        var item = await workspace.AddAsync(node: workspace.Alpha);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Persistence:Provider", "InMemory");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory", ["Deployment:Mode"] = "LocalOnly",
                    ["Security:ApiKey"] = "test-key", ["Security:AllowedWorkspaceRoots:0"] = workspace.Root,
                    ["FileStorage:RootPath"] = Path.Combine(workspace.Root, "uploads"),
                    ["BackboneOutput:RootPath"] = Path.Combine(workspace.Root, "publish"),
                    ["ValidationProfile:Name"] = "fda-ectd-3.2-manual", ["ValidationProfile:Mode"] = "relaxed"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(workspace.Applications);
                services.AddSingleton(workspace.Documents);
                services.AddSingleton<IDocumentPlacementRepository>(workspace.Placements);
                services.AddSingleton(workspace.Nodes);
                services.AddSingleton(workspace.Revisions);
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        var route = $"/api/document-placements/{item.Placement.Id}/section";
        var request = new { nodeInstanceId = workspace.Beta.NodeInstanceId, sortOrder = 7, expectedRevision = 1, ctdSection = "m3.2.s.4.1" };
        using var preview = await client.PostAsJsonAsync(route + "/preview", request);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(1, (await preview.Content.ReadFromJsonAsync<NodePlacementMovePreview>())!.WorkspaceRevision);
        Assert.True(File.Exists(item.Document.StoragePath));
        using var missing = await client.PutAsJsonAsync(route, new { nodeInstanceId = workspace.Beta.NodeInstanceId, ctdSection = "m3.2.s.4.1" });
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using var moved = await client.PutAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var result = await moved.Content.ReadFromJsonAsync<DocumentPlacementDto>();
        Assert.Equal(workspace.Beta.NodeInstanceId, result!.NodeInstanceId);
        Assert.Equal(7, result.SortOrder);
        Assert.Equal(2, result.WorkspaceRevision);
        using var stale = await client.PutAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
