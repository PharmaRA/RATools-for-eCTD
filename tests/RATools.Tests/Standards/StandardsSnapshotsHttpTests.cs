using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RATools.Api.Controllers;
using RATools.Application.Standards;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Standards;

public sealed class StandardsSnapshotsHttpTests
{
    [Fact]
    public async Task ActualHttpCaptureReplaysMetadataAndReadsImmutableResourceWithRangeAndEtag()
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(StandardsSnapshotsController).Assembly.GetName().Name,
            EnvironmentName = "Development",
            ContentRootPath = Path.GetFullPath(".artifacts/p5-06-controller")
        });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(service);
        builder.Services.AddControllers().AddApplicationPart(typeof(StandardsSnapshotsController).Assembly);
        await using var app = builder.Build(); app.MapControllers(); app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            const string route = "/api/ectd-templates/eu-ectd-3.2.2/standards-snapshot";
            using var captured = await client.PostAsync(route, null);
            Assert.Equal(HttpStatusCode.OK, captured.StatusCode);
            var body = await captured.Content.ReadAsStringAsync();
            Assert.DoesNotContain("contentBase64", body, StringComparison.OrdinalIgnoreCase);
            using var view = JsonDocument.Parse(body);
            var id = view.RootElement.GetProperty("id").GetString()!;
            Assert.StartsWith("sha256:", id, StringComparison.Ordinal);
            Assert.Equal("3.1.1", view.RootElement.GetProperty("regionalModule1Version").GetString());
            Assert.Equal(PackageValidationCatalog.EuProfile, view.RootElement.GetProperty("ruleProfileAlias").GetString());
            using var replay = await client.PostAsync(route, null);
            using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.Equal(id, replayBody.RootElement.GetProperty("id").GetString());
            using var loaded = await client.GetAsync("/api/standards-snapshots/" + Uri.EscapeDataString(id));
            Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
            var resource = view.RootElement.GetProperty("resources").EnumerateArray()
                .Single(item => item.GetProperty("key").GetString() == "official-ema-implementation-guide");
            Assert.Equal("OfficialSource", resource.GetProperty("category").GetString());
            Assert.Equal("ae27b9a2258ac396071234539f2bbd88491dec03521c1b12ded135bdf49b2492", resource.GetProperty("sha256").GetString());
            var key = resource.GetProperty("key").GetString()!;
            var digest = resource.GetProperty("sha256").GetString()!;
            var path = "/api/standards-snapshots/" + Uri.EscapeDataString(id) + "/resources/" + Uri.EscapeDataString(key) + "/content";
            using var full = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, full.StatusCode);
            Assert.Equal("application/pdf", full.Content.Headers.ContentType!.MediaType);
            Assert.Equal('"' + digest + '"', full.Headers.ETag!.Tag);
            var bytes = await full.Content.ReadAsByteArrayAsync();
            Assert.Equal(resource.GetProperty("length").GetInt64(), bytes.LongLength);
            Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            using var range = new HttpRequestMessage(HttpMethod.Get, path);
            range.Headers.Range = new RangeHeaderValue(0, 15);
            using var part = await client.SendAsync(range);
            Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
            Assert.Equal(bytes[..16], await part.Content.ReadAsByteArrayAsync());
            using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
            conditional.Headers.IfNoneMatch.Add(full.Headers.ETag);
            using var unchanged = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
            using var missing = await client.GetAsync("/api/standards-snapshots/" + Uri.EscapeDataString(id) + "/resources/unknown/content");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var unsupported = await client.PostAsync("/api/ectd-templates/unknown/standards-snapshot", null);
            Assert.Equal(HttpStatusCode.NotFound, unsupported.StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
