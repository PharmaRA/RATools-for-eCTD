using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Tests.PackageValidation;

namespace RATools.Tests.Api;

public sealed class PackageValidationApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task ExternalZipAndDirectoryProduceQueryableReportFindingsAndExactIdempotency()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        fixture.CreateZip("0000", "same");
        await using var factory = Factory(fixture, enabled: true);
        using var client = factory.CreateClient();
        using var denied = await client.GetAsync("/api/package-validations/profiles");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        var app = Guid.NewGuid();
        using var registration = await client.PostAsJsonAsync("/api/package-validation-inputs", new RegisterPackageInputRequest(app, fixture.PathOf("0000")));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var directory = (await registration.Content.ReadFromJsonAsync<PackageInputDto>(Json))!;
        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent(app.ToString("D")), "applicationId");
        upload.Add(new StringContent("0000"), "sequenceNumber");
        upload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(fixture.ArchivePath("package.zip"))), "file", "external.zip");
        using var uploaded = await client.PostAsync("/api/package-validation-inputs/upload", upload);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var zip = (await uploaded.Content.ReadFromJsonAsync<PackageInputDto>(Json))!;
        using var manifest = JsonDocument.Parse(await client.GetStringAsync($"/api/package-validation-inputs/{zip.Id}/manifest"));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(fixture.ArchivePath("package.zip")))).ToLowerInvariant(),
            manifest.RootElement.GetProperty("capture").GetProperty("archiveSha256").GetString());
        var request = new CreatePackageValidationRequest(directory.Id, PackageValidationCatalog.UsProfile, PackageValidationMode.Development, "external-validation", zip.Id);
        using var created = await client.PostAsJsonAsync("/api/package-validations", request, Json);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var run = (await created.Content.ReadFromJsonAsync<PackageRunSummary>(Json))!;
        using var replay = await client.PostAsJsonAsync("/api/package-validations", request, Json);
        Assert.Equal(run.Id, (await replay.Content.ReadFromJsonAsync<PackageRunSummary>(Json))!.Id);
        using var conflict = await client.PostAsJsonAsync("/api/package-validations", request with { ComparisonInputId = null }, Json);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (run.Status is ValidationRunStatus.Pending or ValidationRunStatus.Running)
        {
            await Task.Delay(50, deadline.Token);
            run = (await client.GetFromJsonAsync<PackageRunSummary>($"/api/package-validations/{run.Id}", Json, deadline.Token))!;
        }
        Assert.Equal(ValidationRunStatus.Completed, run.Status);
        using var report = JsonDocument.Parse(await client.GetStringAsync($"/api/package-validations/{run.Id}/report"));
        Assert.Equal("Completed", report.RootElement.GetProperty("runStatus").GetString());
        Assert.False(report.RootElement.GetProperty("isReadyForFinalization").GetBoolean());
        Assert.Equal(run.Binding.InputManifestDigest, report.RootElement.GetProperty("binding").GetProperty("inputManifestDigest").GetString());
        using var evidence = JsonDocument.Parse(await client.GetStringAsync($"/api/package-validations/{run.Id}/evidence"));
        Assert.Equal("package-rules-v1", evidence.RootElement.GetProperty("ruleCatalog").GetProperty("catalogVersion").GetString());
        var findings = (await client.GetFromJsonAsync<PackageValidationPage<ValidationFinding>>($"/api/package-validations/{run.Id}/findings?limit=1", Json))!;
        Assert.Single(findings.Items);
        Assert.True(findings.TotalCount > 1);
        using var badPage = await client.GetAsync($"/api/package-validations/{run.Id}/findings?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        File.Delete(fixture.PathOf("0000/index.xml"));
        using var retained = await client.GetAsync($"/api/package-validations/{run.Id}/report");
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
    }

    [Fact]
    public async Task QueuedCancellationAndInputSelectionErrorsAreExplicit()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        await using var factory = Factory(fixture, enabled: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        var app = Guid.NewGuid();
        using var ambiguous = await client.PostAsJsonAsync("/api/package-validation-inputs", new RegisterPackageInputRequest(app, fixture.PathOf("")));
        Assert.Equal(HttpStatusCode.Conflict, ambiguous.StatusCode);
        using var response = await client.PostAsJsonAsync("/api/package-validation-inputs", new RegisterPackageInputRequest(app, fixture.PathOf("0000")));
        var input = (await response.Content.ReadFromJsonAsync<PackageInputDto>(Json))!;
        using var created = await client.PostAsJsonAsync("/api/package-validations", new CreatePackageValidationRequest(input.Id,
            PackageValidationCatalog.UsProfile, PackageValidationMode.Development, "cancel-before-start"), Json);
        var run = (await created.Content.ReadFromJsonAsync<PackageRunSummary>(Json))!;
        Assert.Null(run.CoverageSummary);
        using var schema = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var coverageSchema = schema.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("PackageRunSummary").GetProperty("properties").GetProperty("coverageSummary");
        Assert.True(coverageSchema.GetProperty("nullable").GetBoolean());
        Assert.Equal("#/components/schemas/ValidationCoverageSummary", coverageSchema.GetProperty("allOf")[0].GetProperty("$ref").GetString());
        using var cancelled = await client.PostAsync($"/api/package-validations/{run.Id}/cancel", null);
        Assert.Equal(ValidationRunStatus.Cancelled, (await cancelled.Content.ReadFromJsonAsync<PackageRunSummary>(Json))!.Status);
        using var unavailable = await client.GetAsync($"/api/package-validations/{run.Id}/report");
        Assert.Equal(HttpStatusCode.Gone, unavailable.StatusCode);
        using var numericMode = await client.PostAsJsonAsync("/api/package-validations", new { inputId = input.Id, profileSnapshotId = PackageValidationCatalog.UsProfile,
            mode = 0, idempotencyKey = "numeric-mode" });
        Assert.Equal(HttpStatusCode.BadRequest, numericMode.StatusCode);
    }

    private static WebApplicationFactory<Program> Factory(PackageFileInspectorTests.Fixture fixture, bool enabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Persistence:Provider", "InMemory");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory", ["Deployment:Mode"] = "LocalOnly", ["Security:ApiKey"] = "test-key",
                    ["Security:AllowedWorkspaceRoots:0"] = fixture.PathOf(""),
                    ["FileStorage:RootPath"] = fixture.ArchivePath("uploads"), ["BackboneOutput:RootPath"] = fixture.ArchivePath("publish"),
                    ["PackageValidation:StorageRoot"] = fixture.ArchivePath("validation"), ["PackageValidation:Enabled"] = enabled.ToString(),
                    ["PackageValidation:PollInterval"] = "00:00:00.050", ["PackageValidation:HeartbeatInterval"] = "00:00:00.050"
                });
            });
        });
}
