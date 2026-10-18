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
        var xmlPath = Uri.EscapeDataString("0000/index.xml");
        using var preview = JsonDocument.Parse(await client.GetStringAsync($"/api/package-validation-inputs/{zip.Id}/text?logicalPath={xmlPath}&line=2"));
        Assert.Equal("0000/index.xml", preview.RootElement.GetProperty("logicalPath").GetString());
        Assert.Contains("<?xml", preview.RootElement.GetProperty("lines")[0].GetString());
        using var downloaded = await client.GetAsync($"/api/package-validation-inputs/{zip.Id}/file?logicalPath={xmlPath}");
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("application/octet-stream", downloaded.Content.Headers.ContentType!.MediaType);
        Assert.True(downloaded.Headers.TryGetValues("X-Content-Type-Options", out var nosniff));
        Assert.Equal("nosniff", Assert.Single(nosniff!));
        Assert.True(downloaded.Headers.CacheControl!.NoStore);
        Assert.Equal(await File.ReadAllBytesAsync(fixture.PathOf("0000/index.xml")), await downloaded.Content.ReadAsByteArrayAsync());
        using var binaryPreview = await client.GetAsync($"/api/package-validation-inputs/{zip.Id}/text?logicalPath={Uri.EscapeDataString("0000/m2/25-clin-over/overview.pdf")}");
        Assert.Equal(HttpStatusCode.BadRequest, binaryPreview.StatusCode);
        using var badLine = await client.GetAsync($"/api/package-validation-inputs/{zip.Id}/text?logicalPath={xmlPath}&line=0");
        Assert.Equal(HttpStatusCode.BadRequest, badLine.StatusCode);
        using var absent = await client.GetAsync($"/api/package-validation-inputs/{zip.Id}/file?logicalPath=0000%2Fnot-there.xml");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var traversal = await client.GetAsync($"/api/package-validation-inputs/{zip.Id}/file?logicalPath=0000%2F..%2Findex.xml");
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
        using var evidence = JsonDocument.Parse(await client.GetStringAsync($"/api/package-validations/{run.Id}/evidence"));
        Assert.Equal("package-rules-v1", evidence.RootElement.GetProperty("ruleCatalog").GetProperty("catalogVersion").GetString());
        var findings = (await client.GetFromJsonAsync<PackageValidationPage<ValidationFinding>>($"/api/package-validations/{run.Id}/findings?limit=1", Json))!;
        Assert.Single(findings.Items);
        Assert.True(findings.TotalCount > 1);
        using var badPage = await client.GetAsync($"/api/package-validations/{run.Id}/findings?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        using var badLimit = await client.GetAsync($"/api/package-validations/{run.Id}/findings?limit=501");
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
        using var negativeOffset = await client.GetAsync($"/api/package-validations/{run.Id}/findings?offset=-1");
        Assert.Equal(HttpStatusCode.BadRequest, negativeOffset.StatusCode);
        // Category membership comes from the run's archived catalog, not today's rules.
        var all = (await client.GetFromJsonAsync<PackageValidationPage<ValidationFinding>>(
            $"/api/package-validations/{run.Id}/findings?limit=500", Json))!;
        var category = PackageValidationCatalog.Current.Rules[all.Items[0].RuleId].Category;
        var expected = all.Items.Count(finding => PackageValidationCatalog.Current.Rules[finding.RuleId].Category == category);
        var filtered = (await client.GetFromJsonAsync<PackageValidationPage<ValidationFinding>>(
            $"/api/package-validations/{run.Id}/findings?category={Uri.EscapeDataString(category)}&limit=500", Json))!;
        Assert.Equal(expected, filtered.TotalCount);
        Assert.All(filtered.Items, finding => Assert.Equal(category, PackageValidationCatalog.Current.Rules[finding.RuleId].Category));
        var unknownCategory = (await client.GetFromJsonAsync<PackageValidationPage<ValidationFinding>>(
            $"/api/package-validations/{run.Id}/findings?category=NoSuchCategory", Json))!;
        Assert.Equal(0, unknownCategory.TotalCount);
        File.Delete(fixture.PathOf("0000/index.xml"));
        using var changed = await client.GetAsync($"/api/package-validation-inputs/{directory.Id}/text?logicalPath={xmlPath}");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var retainedZip = await client.GetAsync($"/api/package-validation-inputs/{zip.Id}/text?logicalPath={xmlPath}");
        Assert.Equal(HttpStatusCode.OK, retainedZip.StatusCode);
        using var retained = await client.GetAsync($"/api/package-validations/{run.Id}/report");
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
    }

    [Fact]
    public async Task SameSpellingInSequenceAndSurroundingInventoryReturnsOnlyTheExplicitScope()
    {
        await using var fixture = new PackageFileInspectorTests.Fixture();
        var surroundingBytes = await File.ReadAllBytesAsync(fixture.PathOf("0000/index.xml"));
        Directory.CreateDirectory(fixture.PathOf("delivery/0000"));
        var selectedBytes = "<selected-root />"u8.ToArray();
        await File.WriteAllBytesAsync(fixture.PathOf("delivery/0000/index.xml"), selectedBytes);
        await using var factory = Factory(fixture, enabled: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        using var response = await client.PostAsJsonAsync("/api/package-validation-inputs",
            new RegisterPackageInputRequest(Guid.NewGuid(), fixture.PathOf(""), "0000", "delivery/0000"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var input = (await response.Content.ReadFromJsonAsync<PackageInputDto>(Json))!;
        var prefix = $"/api/package-validation-inputs/{input.Id}/file?logicalPath=";
        Assert.Equal(selectedBytes, await client.GetByteArrayAsync(prefix + "0000%2Findex.xml"));
        Assert.Equal(surroundingBytes, await client.GetByteArrayAsync(prefix + "0000%2Findex.xml&surrounding=true"));
        using var wrongScope = await client.GetAsync(prefix + "delivery%2F0000%2Findex.xml&surrounding=true");
        Assert.Equal(HttpStatusCode.NotFound, wrongScope.StatusCode);
        using var traversal = await client.GetAsync(prefix + "..%2F0000%2Findex.xml&surrounding=true");
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
        using var changedClient = factory.CreateClient();
        using var denied = await changedClient.GetAsync(prefix + "0000%2Findex.xml&surrounding=true");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
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
