using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Documents.Dtos;
using RATools.Tests.Workspaces;

namespace RATools.Tests.Api;

public sealed class NodeEditingApiTests
{
    [Fact]
    public async Task NodeUploadsAndBindingsKeepSameNamedManufacturerFilesSeparate()
    {
        using var workspace = new NodeMoveTestWorkspace();
        await workspace.SeedAsync();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Persistence:Provider", "InMemory");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory", ["Deployment:Mode"] = "LocalOnly",
                    ["Security:ApiKey"] = "test-key", ["Security:AllowedWorkspaceRoots:0"] = workspace.Root,
                    ["FileStorage:RootPath"] = Path.Combine(workspace.Root, "uploads"),
                    ["BackboneOutput:RootPath"] = Path.Combine(workspace.Root, "publish")
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
        var route = $"/api/applications/{workspace.Application.Id}/sequences/0000";
        using var missingRevision = await client.PostAsJsonAsync(route + "/nodes", new { definitionKey = "m4-nonclinical-study-reports" });
        Assert.Equal((HttpStatusCode)428, missingRevision.StatusCode);
        using var invalidDefinition = await client.PostAsJsonAsync(route + "/nodes", new { definitionKey = "invented", expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.Conflict, invalidDefinition.StatusCode);
        using var stale = await client.PostAsJsonAsync(route + "/nodes", new { definitionKey = "m4-nonclinical-study-reports", expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        long revision = 1;
        var documents = new List<DocumentDto>();
        var boundPlacements = new List<DocumentPlacementDto>();
        foreach (var node in new[] { workspace.Alpha, workspace.Beta })
        {
            using var content = Upload(node.NodeInstanceId, revision, node == workspace.Alpha ? "Alpha PDF bytes" : "Beta PDF bytes");
            using var uploaded = await client.PostAsync(route + "/documents/upload", content);
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
            var document = (await uploaded.Content.ReadFromJsonAsync<DocumentDto>())!;
            documents.Add(document);
            Assert.Equal("specification.pdf", Path.GetFileName(document.StoragePath));
            revision = document.WorkspaceRevision!.Value;
            using var mismatch = await client.PostAsJsonAsync("/api/document-placements", new { documentId = document.Id,
                applicationId = workspace.Application.Id, sequenceNumber = "0000", ctdSection = "m2.5", operation = "New", nodeInstanceId = node.NodeInstanceId, expectedRevision = revision });
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            using var bound = await client.PostAsJsonAsync("/api/document-placements", new { documentId = document.Id,
                applicationId = workspace.Application.Id, sequenceNumber = "0000", ctdSection = node.CtdSection, operation = "New",
                nodeInstanceId = node.NodeInstanceId, expectedRevision = revision, sortOrder = 4 });
            Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
            var placement = (await bound.Content.ReadFromJsonAsync<DocumentPlacementDto>())!;
            boundPlacements.Add(placement);
            Assert.Equal(node.NodeInstanceId, placement.NodeInstanceId);
            Assert.Equal(4, placement.SortOrder);
            revision = placement.WorkspaceRevision!.Value;
        }
        Assert.NotEqual(documents[0].StoragePath, documents[1].StoragePath);
        Assert.Equal("Alpha PDF bytes", await File.ReadAllTextAsync(documents[0].StoragePath));
        Assert.Equal("Beta PDF bytes", await File.ReadAllTextAsync(documents[1].StoragePath));
        using var duplicateContent = Upload(workspace.Alpha.NodeInstanceId, revision, "must not overwrite");
        using var duplicate = await client.PostAsync(route + "/documents/upload", duplicateContent);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(revision, await workspace.RevisionAsync());
        Assert.Equal("Alpha PDF bytes", await File.ReadAllTextAsync(documents[0].StoragePath));
        using var snapshot = await client.GetAsync(route + "/workspace");
        using var json = System.Text.Json.JsonDocument.Parse(await snapshot.Content.ReadAsStringAsync());
        Assert.Equal(revision, json.RootElement.GetProperty("nodeTree").GetProperty("workspaceRevision").GetInt64());
        Assert.Equal(2, json.RootElement.GetProperty("placements").GetArrayLength());

        var application = (await workspace.Applications.GetAsync(workspace.Application.Id))!;
        application.CreateSequence("0001", "amendment", "Alpha replacement");
        await workspace.Applications.UpdateAsync(application);
        await CtdNodeEditingTests.Service(workspace).InheritAsync(application.Id, "0001", new("0000", 0));
        using var nextUpload = Upload(workspace.Alpha.NodeInstanceId, 1, "Alpha replacement bytes");
        using var nextResponse = await client.PostAsync($"/api/applications/{application.Id}/sequences/0001/documents/upload", nextUpload);
        Assert.Equal(HttpStatusCode.Created, nextResponse.StatusCode);
        var replacementDocument = (await nextResponse.Content.ReadFromJsonAsync<DocumentDto>())!;
        using var nextBinding = await client.PostAsJsonAsync("/api/document-placements", new { documentId = replacementDocument.Id,
            applicationId = application.Id, sequenceNumber = "0001", ctdSection = workspace.Alpha.CtdSection, operation = "New",
            nodeInstanceId = workspace.Alpha.NodeInstanceId, expectedRevision = 2 });
        var replacement = (await nextBinding.Content.ReadFromJsonAsync<DocumentPlacementDto>())!;
        using var wrongTarget = await client.PutAsJsonAsync($"/api/document-placements/{replacement.Id}/metadata", new {
            expectedRevision = 3, operation = "Replace", fileNamePrefix = "specification", lifecycleTargetPlacementId = boundPlacements[1].Id });
        Assert.Equal(HttpStatusCode.Conflict, wrongTarget.StatusCode);
        using var correctTarget = await client.PutAsJsonAsync($"/api/document-placements/{replacement.Id}/metadata", new {
            expectedRevision = 3, operation = "Replace", fileNamePrefix = "specification", lifecycleTargetPlacementId = boundPlacements[0].Id });
        Assert.Equal(HttpStatusCode.OK, correctTarget.StatusCode);
        Assert.Equal(boundPlacements[0].Id, (await correctTarget.Content.ReadFromJsonAsync<DocumentPlacementDto>())!.LifecycleTargetPlacementId);
    }

    private static MultipartFormDataContent Upload(Guid nodeId, long revision, string text)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(text), "file", "specification.pdf");
        form.Add(new StringContent("m3.2.s.4.1"), "CtdSection");
        form.Add(new StringContent(nodeId.ToString()), "NodeInstanceId");
        form.Add(new StringContent(revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), "ExpectedRevision");
        return form;
    }
}
