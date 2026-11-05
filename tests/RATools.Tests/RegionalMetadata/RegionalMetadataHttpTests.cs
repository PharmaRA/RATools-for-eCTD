using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using RATools.Api.Controllers;
using RATools.Api.Middleware;
using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.RegionalMetadata;

public sealed class RegionalMetadataHttpTests
{
    [Theory]
    [InlineData("defaults")]
    [InlineData("preflight")]
    [InlineData("mixed")]
    public async Task XmlForbiddenInputReturnsBadRequestWithoutWriting(string operation)
    {
        var rig = await RegionalMetadataIntegrationRig.CreateAsync(); await using var host = RegionalEditingHttpHost.Create(rig);
        await host.StartAsync(); using var client = RegionalEditingHttpHost.Client(host); var baseline = await rig.RevisionAsync();
        var valid = RegionalEditingHttpHost.Content(); var invalid = valid with { Eu = new([valid.Eu!.Envelopes[0] with { Applicant = "Forbidden\0value" }]) };
        var sequence = $"/api/applications/{rig.App.Id}/sequences/0001/publishing-metadata";
        using var response = operation switch
        {
            "defaults" => await client.PutAsJsonAsync($"/api/applications/{rig.App.Id}/regional-metadata/defaults", new { expectedDefaultsRevision = 0,
                standardsSnapshotId = rig.Snapshot.Id, defaults = new RegionalApplicationDefaultsContent(1, "EU", Eu: new("Forbidden\0value", [], [], [])) }),
            "preflight" => await client.PostAsJsonAsync(sequence + "/regional/preflight", new { standardsSnapshotId = rig.Snapshot.Id, content = invalid }),
            _ => await client.PutAsJsonAsync(sequence, RegionalEditingHttpHost.Request(baseline, rig.Snapshot.Id) with { Regional = new(rig.Snapshot.Id, invalid) })
        };
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(baseline, await rig.RevisionAsync()); Assert.Null(await rig.Service.GetSequenceAsync(rig.App.Id, "0001"));
        Assert.Null(await rig.Service.GetApplicationAsync(rig.App.Id));
    }

    [Fact]
    public async Task RealHttpBinderRejectsDuplicatesAndEvidenceAndMapsRequiredAndStaleRevisions()
    {
        var rig = await RegionalMetadataIntegrationRig.CreateAsync();
        await using var host = RegionalEditingHttpHost.Create(rig); await host.StartAsync();
        using var client = RegionalEditingHttpHost.Client(host); var path = $"/api/applications/{rig.App.Id}/sequences/0001/publishing-metadata";
        var baseline = await rig.RevisionAsync();
        foreach (var body in new[] { "{\"expectedRevision\":1,\"expectedRevision\":2}", "{\"provenance\":[]}", "{\"regional\":{\"content\":{\"schemaVersion\":1,\"SchemaVersion\":1}}}" })
        {
            using var response = await client.PutAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var request = RegionalEditingHttpHost.Request(baseline, rig.Snapshot.Id);
        using var missing = await client.PutAsJsonAsync(path, request with { ExpectedRevision = null }); Assert.Equal(428, (int)missing.StatusCode);
        using var saved = await client.PutAsJsonAsync(path, request); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = (await saved.Content.ReadFromJsonAsync<SequencePublishingMetadataDto>())!;
        Assert.Equal(baseline + 1, dto.WorkspaceRevision); Assert.Equal(dto.WorkspaceRevision, dto.Regional!.WorkspaceRevision);
        using var stale = await client.PutAsJsonAsync(path, request); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("WorkspaceRevisionConflict", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(baseline + 1, await rig.RevisionAsync());
    }

    [Fact]
    public async Task RealHttpPreflightAndDefaultsAbsenceDoNotMutateAndPreviewReturnsExactSource()
    {
        var rig = await RegionalMetadataIntegrationRig.CreateAsync(); await using var host = RegionalEditingHttpHost.Create(rig);
        await host.StartAsync(); using var client = RegionalEditingHttpHost.Client(host);
        var app = $"/api/applications/{rig.App.Id}/regional-metadata";
        var seq = $"/api/applications/{rig.App.Id}/sequences/0001/publishing-metadata/regional"; var baseline = await rig.RevisionAsync();
        using var absent = await client.GetAsync(app + "/defaults"); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var schema = await client.GetAsync(app + "/schema"); Assert.Equal(HttpStatusCode.OK, schema.StatusCode);
        using var check = await client.PostAsJsonAsync(seq + "/preflight", new { standardsSnapshotId = rig.Snapshot.Id, content = RegionalEditingHttpHost.Content() });
        Assert.Equal(HttpStatusCode.OK, check.StatusCode); Assert.Contains("eu.envelopes", await check.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(await rig.Service.GetSequenceAsync(rig.App.Id, "0001")); Assert.Equal(baseline, await rig.RevisionAsync());
        var original = await rig.SaveAsync(RegionalEditingHttpHost.Content());
        var defaults = await rig.Service.SaveApplicationAsync(rig.App.Id, 0, rig.Snapshot.Id, new(1, "EU", Eu: new("Exact inherited", [], [], [])));
        using var preview = await client.PostAsJsonAsync(seq + "/inheritance/preview", new { expectedRevision = original.WorkspaceRevision, defaultsRevisionId = defaults.Id });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var diff = (await preview.Content.ReadFromJsonAsync<RegionalMetadataPreviewDto>())!;
        Assert.Equal(defaults.Id, diff.SourceRevisionId); Assert.Equal(defaults.Digest, diff.SourceDigest);
        Assert.Equal(original.Id, (await rig.Service.GetSequenceAsync(rig.App.Id, "0001"))!.Id);
        using var foreign = await client.GetAsync($"/api/applications/{rig.OtherApp.Id}/regional-metadata/revisions/{original.Id}?sequenceNumber=0001");
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }
}

internal static class RegionalEditingHttpHost
{
    internal static WebApplication Create(RegionalMetadataIntegrationRig rig)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(ApplicationsController).Assembly.GetName().Name,
            EnvironmentName = "Development", ContentRootPath = rig.Root });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ApplicationsController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new RegionalControllerSelection())).AddControllersAsServices();
        var transaction = new InMemoryPersistenceTransaction();
        var profiles = new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]);
        var shared = new SequencePublishingMetadataService(rig.Applications, profiles, new WorkspaceMutationCoordinator(rig.Revisions, transaction), rig.Service);
        builder.Services.Replace(ServiceDescriptor.Singleton(new RegionalMetadataController(rig.Service)));
        builder.Services.Replace(ServiceDescriptor.Singleton(new ApplicationsController(null!, null!, null!, shared, null!)));
        var host = builder.Build(); host.UseMiddleware<GlobalExceptionMiddleware>(); host.MapControllers(); host.Urls.Add("http://127.0.0.1:0"); return host;
    }
    internal static HttpClient Client(WebApplication host) => new() { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    internal static RegionalMetadataContent Content() => new(1, "EU", Eu: new([
        new(Guid.NewGuid(), "explicit-identifier", "de", "DE-BFARM", "national", "maa", null, null, "initial", "Original applicant",
            [new(Guid.NewGuid(), "DE-2"), new(Guid.NewGuid(), "DE-1")], [new(Guid.NewGuid(), "Product B"), new(Guid.NewGuid(), "Product A")], [], [new(Guid.NewGuid(), "0001")], "Explicit description") ]));
    internal static RATools.Application.Applications.Requests.UpdateSequencePublishingMetadataRequest Request(long revision, string snapshot) =>
        new(null, "initial", null, "Shared saved", "Shared applicant", null, null, null, null, null, null, revision,
            new RegionalMetadataUpdate(snapshot, Content()));
    private sealed class RegionalControllerSelection : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            for (var index = feature.Controllers.Count - 1; index >= 0; index--)
                if (feature.Controllers[index] != typeof(ApplicationsController).GetTypeInfo() && feature.Controllers[index] != typeof(RegionalMetadataController).GetTypeInfo())
                    feature.Controllers.RemoveAt(index);
        }
    }
}
