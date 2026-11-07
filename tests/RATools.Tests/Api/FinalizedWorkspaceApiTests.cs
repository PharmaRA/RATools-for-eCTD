using RATools.Tests.PackageValidation;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.Api;

// P4-07 at the HTTP boundary: a finalized sequence rejects every draft write with a
// machine-readable conflict, exposes its state on the workspace, and only becomes
// editable again through the explicit draft-continuation endpoint.
public sealed class FinalizedWorkspaceApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private readonly WebApplicationFactory<Program> _factory;

    public FinalizedWorkspaceApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task FinalizedSequenceRejectsWritesUntilAnExplicitDraftIsOpened()
    {
        using var tempRoot = new TemporaryDirectory();
        var host = CreateHost(tempRoot.Path);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        var application = await CreateApplicationAsync(client, tempRoot.Path);
        var sequenceUrl = $"/api/applications/{application.Id}/sequences/0000";

        var store = host.Services.GetRequiredService<IFinalizationStore>();
        var snapshot = FinalizedSequence.Create(application.Id, "0000", 0, Digest("manifest"),
            PackageValidationCatalog.UsProfile, "3.2.2", null, Digest("history"), Guid.NewGuid(),
            Digest("input"), Digest("report"), PackageValidationMode.Development, false, [],
            FinalizationService.ToolVersion, "finalize-api", Now);
        await store.RegisterAsync(snapshot);

        // Every ordinary draft write is refused with an explicit boundary, not a stale
        // revision or a generic 500.
        var blocked = await client.PutAsJsonAsync($"{sequenceUrl}/publishing-metadata",
            new { ApplicantName = "Must not be written", ExpectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var boundary = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("WorkspaceFinalized", boundary.GetProperty("code").GetString());
        Assert.Equal(snapshot.Id, boundary.GetProperty("finalizedSequenceId").GetGuid());
        Assert.Equal(0, boundary.GetProperty("sourceWorkspaceRevision").GetInt64());

        var workspace = await client.GetFromJsonAsync<JsonElement>($"{sequenceUrl}/workspace");
        Assert.Equal(0, workspace.GetProperty("workspaceRevision").GetInt64());
        var finalization = workspace.GetProperty("finalization");
        Assert.False(finalization.GetProperty("isEditable").GetBoolean());
        Assert.Equal(snapshot.Id, finalization.GetProperty("finalizedSequenceId").GetGuid());

        // Draft deletion would orphan the delivered evidence.
        var delete = await client.DeleteAsync($"{sequenceUrl}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);

        // Continuation is the only write: it names the snapshot and advances the revision.
        var opened = await client.PostAsJsonAsync($"/api/finalized-sequences/{snapshot.Id}/draft-revisions",
            new { ExpectedRevision = 0, IdempotencyKey = "continue-1" });
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var draft = await opened.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(snapshot.Id, draft.GetProperty("draft").GetProperty("finalizedSequenceId").GetGuid());
        Assert.Equal(1, draft.GetProperty("draft").GetProperty("workspaceRevision").GetInt64());
        Assert.True(draft.GetProperty("created").GetBoolean());

        var editable = await client.GetFromJsonAsync<JsonElement>($"{sequenceUrl}/workspace");
        Assert.Equal(1, editable.GetProperty("workspaceRevision").GetInt64());
        Assert.True(editable.GetProperty("finalization").GetProperty("isEditable").GetBoolean());

        var written = await client.PutAsJsonAsync($"{sequenceUrl}/publishing-metadata", new
        {
            SubmissionType = "original-application",
            SequenceDescription = "Continued draft",
            ApplicantName = "Continued draft",
            ExpectedRevision = 1
        });
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);

        // The draft is unique per snapshot and an exact replay is idempotent.
        var replay = await client.PostAsJsonAsync($"/api/finalized-sequences/{snapshot.Id}/draft-revisions",
            new { ExpectedRevision = 1, IdempotencyKey = "continue-1" });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.False((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetBoolean());
        var second = await client.PostAsJsonAsync($"/api/finalized-sequences/{snapshot.Id}/draft-revisions",
            new { ExpectedRevision = 1, IdempotencyKey = "continue-2" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("FINALIZATION_DRAFT_ALREADY_OPEN",
            (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var read = await client.GetFromJsonAsync<JsonElement>($"/api/finalized-sequences/{snapshot.Id}/draft-revisions");
        Assert.Equal("continue-1", read.GetProperty("idempotencyKey").GetString());
    }

    [Fact]
    public async Task ManifestRequiresAReadableCaptureAndAnExistingSnapshot()
    {
        using var tempRoot = new TemporaryDirectory();
        var host = CreateHost(tempRoot.Path);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        var application = await CreateApplicationAsync(client, tempRoot.Path);
        var store = host.Services.GetRequiredService<IFinalizationStore>();
        var snapshot = FinalizedSequence.Create(application.Id, "0000", 0, Digest("manifest"),
            PackageValidationCatalog.UsProfile, "3.2.2", null, Digest("history"), Guid.NewGuid(),
            Digest("input"), Digest("report"), PackageValidationMode.Development, false, [],
            FinalizationService.ToolVersion, "manifest-api", Now);
        await store.RegisterAsync(snapshot);

        // A development snapshot registered by a fixture has no captured release; the
        // endpoint reports the missing evidence instead of inventing a manifest.
        var missing = await client.GetAsync($"/api/finalizations/{snapshot.Id}/manifest");
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/finalizations/{Guid.NewGuid()}/manifest")).StatusCode);
    }

    // P5-04 at the HTTP boundary: the preview serves the captured bytes of one verified
    // release, supports Range, and refuses anything the snapshot did not record.
    [Fact]
    public async Task PreviewServesOnlyCapturedReleaseMembersWithARangeAndAStrongEtag()
    {
        using var tempRoot = new TemporaryDirectory();
        var host = CreateHost(tempRoot.Path);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        var application = await CreateApplicationAsync(client, tempRoot.Path);

        var captureStore = host.Services.GetRequiredService<IReleaseCaptureStore>();
        var staging = await captureStore.SaveAsync(new ReleaseCapturePlan(application.Id, "0000",
        [
            ReleaseCaptureFile.Generated("payload/0000/index.xml", Encoding.UTF8.GetBytes("<index />")),
            ReleaseCaptureFile.Generated("payload/0000/m2/overview.pdf", Encoding.UTF8.GetBytes("%PDF-1.4 preview bytes")),
            ReleaseCaptureFile.Generated("validation-report.json", Encoding.UTF8.GetBytes("{}"),
                ReleaseFileKind.ValidationReport)
        ]));
        var manifest = new ReleaseManifest(application.Id, "0000", 0, FinalizationService.ToolVersion,
            Digest("input"), JsonSerializer.SerializeToElement(new
            {
                applicationId = application.Id.ToString("D"), targetSequence = "0000", digest = Digest("history"),
                entries = Array.Empty<object>()
            }), Digest("history"),
            new ReleaseValidationReference(Digest("report"), "Development", PackageValidationCatalog.UsProfile, "3.2.2",
                null, Digest("rules"), "engine", null), [], staging.Files, [], [], []);
        await captureStore.CompleteAsync(staging, manifest);
        await captureStore.CommitAsync(staging, manifest.Digest);

        var store = host.Services.GetRequiredService<IFinalizationStore>();
        var snapshot = FinalizedSequence.Create(application.Id, "0000", 0, manifest.Digest,
            PackageValidationCatalog.UsProfile, "3.2.2", null, Digest("history"), Guid.NewGuid(),
            Digest("input"), Digest("report"), PackageValidationMode.Development, false, [],
            FinalizationService.ToolVersion, "preview-api", Now);
        await store.RegisterAsync(snapshot);

        var xmlSha = manifest.Files.Single(file => file.RelativePath == "payload/0000/index.xml").Sha256;
        var preview = await client.GetAsync(
            $"/api/finalizations/{snapshot.Id}/files?path=payload/0000/index.xml");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("application/xml", preview.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"\"{xmlSha}\"", preview.Headers.ETag!.Tag);
        // The response may be cached only by this authenticated client, and must be
        // revalidated against the strong ETag before any reuse.
        Assert.True(preview.Headers.CacheControl!.NoCache);
        Assert.True(preview.Headers.CacheControl!.Private);
        Assert.Equal("<index />", await preview.Content.ReadAsStringAsync());
        // A non-PDF is delivered as an attachment, so released bytes are never rendered.
        Assert.Equal("attachment", preview.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("index.xml", preview.Content.Headers.ContentDisposition.FileName);

        // A Range read of the PDF is a partial response from the same immutable bytes.
        using var ranged = new HttpRequestMessage(HttpMethod.Get,
            $"/api/finalizations/{snapshot.Id}/files?path=payload/0000/m2/overview.pdf");
        ranged.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
        var partial = await client.SendAsync(ranged);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("application/pdf", partial.Content.Headers.ContentType!.MediaType);
        Assert.Equal("%PDF", await partial.Content.ReadAsStringAsync());
        // The PDF is meant to render in a viewer, so it is served inline without a download name.
        Assert.Null(partial.Content.Headers.ContentDisposition);

        // A directory entry that is not a manifest member is a 404, not a directory listing.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/finalizations/{snapshot.Id}/files?path=payload/0000/m3")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(
            $"/api/finalizations/{snapshot.Id}/files?path=../manifest.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/finalizations/{Guid.NewGuid()}/files?path=payload/0000/index.xml")).StatusCode);

        // Editing delivered bytes after capture makes the release unreadable evidence.
        var releaseDirectory = Path.Combine(tempRoot.Path, application.Id.ToString("N"), "_releases", "0000",
            manifest.Digest, "payload", "0000");
        await File.WriteAllTextAsync(Path.Combine(releaseDirectory, "index.xml"), "<index>edited</index>");
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(
            $"/api/finalizations/{snapshot.Id}/files?path=payload/0000/index.xml")).StatusCode);
    }

    private WebApplicationFactory<Program> CreateHost(string root) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Persistence:Provider", "InMemory");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.Sources.Clear();
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Deployment:Mode"] = "LocalOnly",
                    ["Swagger:Enabled"] = "false",
                    ["Security:ApiKey"] = "test-key",
                    ["Security:AllowedWorkspaceRoots:0"] = root,
                    ["FileStorage:RootPath"] = root,
                    ["BackboneOutput:RootPath"] = root
                });
            });
        });

    private static async Task<ApplicationResponse> CreateApplicationAsync(HttpClient client, string root)
    {
        var created = await client.PostAsJsonAsync("/api/applications", new
        {
            ApplicationNumber = "IND-407",
            EctdTemplateKey = "us-fda-ectd-3.2.2",
            SponsorName = "Boundary Sponsor",
            WorkingDirectoryParentPath = root
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var application = (await created.Content.ReadFromJsonAsync<ApplicationResponse>())!;
        var sequence = await client.PostAsJsonAsync($"/api/applications/{application.Id}/sequences", new
        {
            SequenceNumber = "0000",
            SubmissionType = "original-application",
            Description = "Finalized boundary"
        });
        Assert.Equal(HttpStatusCode.OK, sequence.StatusCode);
        return application;
    }

    private sealed record ApplicationResponse(Guid Id);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = RetainedPackageTestFiles.Create();
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) RetainedPackageTestFiles.RetainDirectory(Path);
        }
    }

    private static string Digest(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant();
}
