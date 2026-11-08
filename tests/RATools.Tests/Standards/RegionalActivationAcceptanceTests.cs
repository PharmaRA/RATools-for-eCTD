using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RATools.Api.Controllers;
using RATools.Application.Abstractions.Security;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.Applications;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.Standards;

/// <summary>Independent acceptance of published snapshot bytes and persisted admission gates.</summary>
public sealed class RegionalActivationAcceptanceTests
{
    private static readonly string[] UsIds = ["US-ADMIN-METADATA", "US-FORM-ATTRIBUTES", "US-SUBMISSION-SCOPE"];
    private static readonly string[] EuIds = ["EU-ENVELOPE-METADATA", "EU-PI-CONTEXT", "EU-RELATED-SEQUENCE"];

    [Fact]
    public async Task ActualHttpCapturesExposeDistinctRegionalRulesAndRetainRequiredQualificationGaps()
    {
        var store = new InMemoryStandardsSnapshotStore();
        var service = new StandardsSnapshotService(store, new CompositeStandardsProfileProvider(
            [new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]));
        var runs = new InMemoryPackageValidationStore();
        var applications = new InMemoryApplicationRepository();
        var reader = new AdmissionOnlyReader();
        var validation = new PackageValidationService(runs, reader, new AdmissionOnlyPaths(),
            new(new PdfPigPackageInspector()), snapshots: service, applications: applications);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(StandardsSnapshotsController).Assembly.GetName().Name,
            EnvironmentName = "Development",
            ContentRootPath = Path.GetFullPath(".")
        });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton(validation);
        builder.Services.AddSingleton<IPackageValidationStore>(runs);
        builder.Services.AddControllers().AddApplicationPart(typeof(StandardsSnapshotsController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
        await using var app = builder.Build(); app.MapControllers(); app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            foreach (var template in new[] { "us-fda-ectd-3.2.2", "eu-ectd-3.2.2" })
            {
                using var capture = await client.PostAsync("/api/ectd-templates/" + template + "/standards-snapshot", null);
                Assert.Equal(HttpStatusCode.OK, capture.StatusCode);
                using var view = JsonDocument.Parse(await capture.Content.ReadAsStringAsync());
                var id = view.RootElement.GetProperty("id").GetString()!;
                var alias = view.RootElement.GetProperty("ruleProfileAlias").GetString()!;
                var rules = view.RootElement.GetProperty("resources").EnumerateArray()
                    .Single(resource => resource.GetProperty("key").GetString() == "rules");
                using var response = await client.GetAsync("/api/standards-snapshots/" + Uri.EscapeDataString(id) + "/resources/rules/content");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(rules.GetProperty("sha256").GetString(), Sha(bytes));
                using var stream = new MemoryStream(bytes, writable: false);
                var catalog = PackageValidationCatalog.Load(stream);
                var selected = catalog.ForProfile(alias);
                var expected = template.StartsWith("us-", StringComparison.Ordinal) ? UsIds : EuIds;
                var other = ReferenceEquals(expected, UsIds) ? EuIds : UsIds;
                foreach (var ruleId in expected)
                {
                    var rule = Assert.Single(selected, rule => rule.InternalRuleId == ruleId);
                    Assert.True(rule.RequiredForReadiness);
                    Assert.Equal(ImplementationStatus.Implemented, rule.ImplementationStatus);
                    Assert.Equal(RuleSourceKind.ProjectPolicy, catalog.Sources[rule.SourceId].Kind);
                    Assert.Null(rule.AuthorityRuleId);
                    Assert.NotEmpty(rule.SourceSection);
                }
                Assert.DoesNotContain(selected, rule => other.Contains(rule.InternalRuleId, StringComparer.Ordinal));
                var inventory = Assert.Single(selected, rule => rule.InternalRuleId.EndsWith("-CRITERIA-INVENTORY", StringComparison.Ordinal));
                Assert.True(inventory.RequiredForReadiness);
                Assert.Equal(ImplementationStatus.NotImplemented, inventory.ImplementationStatus);
                Assert.False(catalog.Profiles[alias].QualifiedForFinalization);
                var snapshot = await service.GetAsync(id);
                var context = template.StartsWith("us-", StringComparison.Ordinal)
                    ? new StandardsBusinessContext("nda", "national", "US", "en", "m1.1", "new")
                    : new StandardsBusinessContext("maa", "national", "DE", "de", "m1", "new");
                Assert.Contains(snapshot.Evaluate(context, DateOnly.FromDateTime(snapshot.AcquiredUtc), template),
                    reason => reason == "EvidenceIncomplete: ExternalComparison");
                var application = new SubmissionApplication("123456", template.StartsWith("us-", StringComparison.Ordinal) ? "US" : "EU",
                    "Synthetic admission fixture", Path.GetFullPath(".artifacts"), template);
                application.CreateSequence("0001", "initial", "Admission-only fixture");
                await applications.AddAsync(application);
                var inputId = Guid.NewGuid();
                var manifest = new PackageInputManifest(application.Id, "0001", [], [], [], []);
                await runs.RegisterInputAsync(new(inputId, application.Id, "0001", Path.GetFullPath(".artifacts"), null,
                    PackageSourceKind.Directory, manifest.Digest, JsonSerializer.Serialize(manifest), new(), DateTime.UtcNow, false));
                var request = new CreatePackageValidationRequest(inputId, alias, PackageValidationMode.Formal,
                    "formal-" + Guid.NewGuid().ToString("N"), StandardsSnapshotId: id, StandardsBusinessContext: context);
                var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
                using var rejected = await client.PostAsJsonAsync("/api/package-validations", request, json);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                using var refusal = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                Assert.True(refusal.RootElement.TryGetProperty("code", out var code), "Formal refusal requires a stable API code.");
                Assert.Equal("STANDARDS_SELECTION_BLOCKED", code.GetString());
                var reasons = refusal.RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ToArray();
                Assert.Contains("EvidenceIncomplete: ExternalComparison", reasons);
                Assert.Contains(reasons, reason => reason!.StartsWith("RegionalBusinessContextUnconfirmed:", StringComparison.Ordinal));
                Assert.DoesNotContain(reasons, reason => reason!.Contains("P6-03 remains pending", StringComparison.Ordinal));
                Assert.Equal(0, (await runs.ListRunsAsync(application.Id, 0, 10)).TotalCount);
                using var trial = await client.PostAsJsonAsync("/api/package-validations", request with
                    { Mode = PackageValidationMode.Development, IdempotencyKey = "trial-" + Guid.NewGuid().ToString("N") }, json);
                Assert.Equal(HttpStatusCode.Accepted, trial.StatusCode);
                var queued = (await trial.Content.ReadFromJsonAsync<PackageRunSummary>(json))!;
                Assert.Equal(id, queued.Binding.StandardsSnapshotId);
                Assert.Contains("package-regional-inspector-v2", queued.Binding.EngineVersion, StringComparison.Ordinal);
                Assert.Equal(0, reader.ReadCount);
            }
        }
        finally { await app.StopAsync(); }
    }

    [RequiresPostgresFact]
    public async Task ActiveFlagAndVerifiedScopeCannotBypassMissingEvidenceAfterNewDatabaseConnection()
    {
        StandardsProfileSnapshot saved;
        await using (var database = new PostgresFixture().CreateDbContext())
        {
            var captured = await new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(database),
                new EuEctd322StandardsProfileProvider()).CaptureAsync("eu-ectd-3.2.2");
            // Synthetic admission attempt. This deliberately changes no evidence status.
            saved = StandardsProfileSnapshot.Create(captured.Descriptor with
            {
                DisplayName = "Synthetic active-only gate attempt " + Guid.NewGuid().ToString("N"),
                Lifecycle = captured.Lifecycle with { Status = StandardsSnapshotStatus.Active },
                Scopes = [new("maa", "national", "DE", "de", "m1", "new", true, true, true, "Verified")]
            }, captured.Resources, captured.AcquiredUtc);
            await new EfCoreStandardsSnapshotStore(database).AddAsync(saved);
        }
        await using var reopened = new PostgresFixture().CreateDbContext();
        var restored = (await new EfCoreStandardsSnapshotStore(reopened).GetAsync(saved.Id))!;
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(restored));
        var context = new StandardsBusinessContext("maa", "national", "DE", "de", "m1", "new");
        var reasons = restored.Evaluate(context, DateOnly.FromDateTime(restored.AcquiredUtc), restored.TemplateKey);
        Assert.DoesNotContain(reasons, reason => reason.StartsWith("ProfileNotActive:", StringComparison.Ordinal));
        Assert.DoesNotContain(reasons, reason => reason.StartsWith("BusinessScopeNotQualified:", StringComparison.Ordinal));
        foreach (var category in new[] { "Sources", "ControlledVocabulary", "MetadataMapping", "Writer", "Rules", "ExternalComparison" })
            Assert.Contains("EvidenceIncomplete: " + category, reasons);
        Assert.Contains(reasons, reason => reason.StartsWith("SourceVerificationIncomplete:", StringComparison.Ordinal));
        using var rules = new MemoryStream(restored.Resources.Single(resource => resource.Key == "rules").ReadBytes());
        var catalog = PackageValidationCatalog.Load(rules);
        foreach (var ruleId in EuIds) Assert.Contains(catalog.ForProfile(restored.RuleProfileAlias), rule => rule.InternalRuleId == ruleId);
        Assert.Equal(saved.Resources.Single(resource => resource.Key == "rules").Sha256,
            restored.Resources.Single(resource => resource.Key == "rules").Sha256);
    }

    [Fact]
    public void ChangedRegionalBehaviorCannotReusePreRegionalEngineIdentity()
    {
        const string old = "package-delivery-v3;package-lifecycle-inspector-v1;package-file-inspector-v2;package-pdf-validator-v1;independent-pdf-v1";
        var current = PackageDeliveryInspector.VersionFor("independent-pdf-v1");
        Assert.NotEqual(old, current);
        Assert.StartsWith("package-delivery-v4;", current, StringComparison.Ordinal);
        Assert.Contains("package-regional-inspector-v2", current, StringComparison.Ordinal);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class AdmissionOnlyReader : IPackageInputReader
    {
        public int ReadCount { get; private set; }
        public Task<ICapturedPackageInput> ReadAsync(PackageInputSelection selection, PackageReadLimits limits, CancellationToken cancellationToken = default)
        { ReadCount++; throw new InvalidOperationException("Admission must not read mutable package bytes."); }
    }
    private sealed class AdmissionOnlyPaths : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];
        public string EnsureAllowed(string path) => throw new InvalidOperationException("Admission must not inspect file paths.");
    }
}
