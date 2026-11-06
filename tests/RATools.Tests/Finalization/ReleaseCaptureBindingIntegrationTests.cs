using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Standards;
using RATools.Domain.Common;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Finalization;

public sealed class ReleaseCaptureBindingIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactWriterOutputIsRequiredWhenReusingACompletedRunAtANewRevision(bool changeRegionalMetadata)
        => await VerifyAsync(changeRegionalMetadata, new InMemoryPackageValidationStore());

    internal static async Task VerifyAsync(bool changeRegionalMetadata, IPackageValidationStore validationStore)
    {
        var fixture = await RegionalMetadataCaptureTests.CreateAsync();
        var original = await fixture.Planner.PlanAsync(fixture.Request, fixture.Run, fixture.Stored);
        var payload = original.Capture.Files.Where(file => file.Kind == ReleaseFileKind.Payload).ToArray();
        var inventory = new PackageInputManifest(fixture.Request.ApplicationId, "0001", payload.Select(file =>
            new PackageInputFile(file.RelativePath[8..], file.Length, file.Sha256, file.Md5)), [], [], []);
        var inputDigest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, manifestDigest = inventory.Digest, archiveSha256 = (string?)null
        }));
        var manifest = JsonSerializer.SerializeToNode(inventory, JsonOptions)!.AsObject();
        var limits = new PackageReadLimits();
        manifest["capture"] = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 1, inputDigest, archiveSha256 = (string?)null,
            sourceKind = PackageSourceKind.Directory, limitsDigest = limits.Digest()
        }, JsonOptions);
        var inputId = Guid.NewGuid();
        await validationStore.RegisterInputAsync(new(inputId, inventory.ApplicationId, inventory.SequenceNumber,
            "/unused-memory-input", null, PackageSourceKind.Directory, inputDigest, manifest.ToJsonString(JsonOptions), limits,
            DateTime.UtcNow, false));
        var previous = fixture.Run.Binding;
        var binding = new ValidationBinding(inputDigest, previous.HistoryBaselineDigest, previous.ProfileSnapshotId,
            previous.RulesDigest, previous.EngineVersion, previous.ValidationMode, previous.ResourceLimitsDigest,
            previous.StandardsSnapshotId, previous.SelectionDate, previous.StandardsBusinessContext);
        var request = fixture.Stored with { InputId = inputId, RuleCatalog = PackageValidationCatalog.ExportSnapshot(), StandardsSnapshotId = binding.StandardsSnapshotId,
            SelectionDate = binding.SelectionDate, StandardsBusinessContext = binding.StandardsBusinessContext };
        var now = DateTime.UtcNow;
        var run = new PackageValidationRun(inventory.ApplicationId, "0001", binding,
            JsonSerializer.Serialize(request, JsonOptions), Guid.NewGuid().ToString("N"), now);
        await validationStore.EnqueueAsync(run);
        var lease = (await validationStore.ClaimAsync("qa01", now, TimeSpan.FromMinutes(1)))!;
        var report = ValidationReport.Create(PackageValidationCatalog.Current, binding, ValidationRunStatus.Completed, []);
        Assert.True(await validationStore.CompleteAsync(run.Id, lease.Token, lease.Owner, now, report));
        var package = fixture.Package with { WorkspaceRevision = 2 };
        if (changeRegionalMetadata)
        {
            var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), package.ApplicationId, "0001", 2, 2,
                fixture.Revision.StandardsSnapshotId,
                fixture.Revision.Content! with { Us = fixture.Revision.Content.Us! with { ApplicantName = "Changed applicant" } },
                null, [], [], null, null, [new("us.applicantName", "UserEntered")], [], null, null, now);
            package = package with { RegionalMetadata = revision };
        }
        var planner = new EctdReleaseCapturePlanner(new FixedPackage(package), new IchIndexXmlWriter(),
            new RegionalBackboneWriterRegistry([new UsRegionalBackboneWriter(new UsRegionalXmlWriter())]),
            new FdaEctd322StandardsProfileProvider(), fixture.Snapshots);
        var captureStore = new MemoryCaptureStore();
        var service = new ReleaseCaptureService(validationStore, planner, captureStore, fixture.Snapshots);
        var finalRequest = fixture.Request with { PackageValidationRunId = run.Id, ExpectedRevision = 2 };

        if (changeRegionalMetadata)
        {
            await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => service.CaptureAsync(finalRequest));
            Assert.Null(captureStore.Plan);
        }
        else
        {
            var capture = await service.CaptureAsync(finalRequest);
            Assert.Equal(run.Id, capture.ValidationRunId);
            Assert.Equal(2, Assert.IsType<FinalizationCapture>(capture).Manifest.SourceWorkspaceRevision);
            Assert.Equal(payload.Length, captureStore.Plan!.Files.Count(file => file.Kind == ReleaseFileKind.Payload));
            Assert.Equal(report.Binding, binding);
        }
    }

    private sealed class FixedPackage(EctdSequencePackage package) : IStandardsBoundPackageModelBuilder
    {
        public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default) => Task.FromResult(package);
        public Task<EctdSequencePackage> BuildWithStandardsAsync(BuildEctdPackageRequest request, StandardsProfile profile,
            CancellationToken cancellationToken = default) => Task.FromResult(package);
    }

    private sealed class MemoryCaptureStore : IReleaseCaptureStore
    {
        public ReleaseCapturePlan? Plan { get; private set; }
        public Task<ReleaseCaptureStaging> SaveAsync(ReleaseCapturePlan plan, CancellationToken cancellationToken = default)
        {
            Plan = plan;
            foreach (var file in plan.Files)
            {
                Assert.NotNull(file.Content);
                Assert.Equal(file.Sha256, Convert.ToHexString(SHA256.HashData(file.Content!)).ToLowerInvariant());
            }
            return Task.FromResult(new ReleaseCaptureStaging(plan.ApplicationId, plan.SequenceNumber, "/memory",
                plan.Files.Select(file => new ReleaseManifestFile(file.RelativePath,file.Length,file.Sha256,file.Md5,file.Kind)).ToArray()));
        }
        public Task CompleteAsync(ReleaseCaptureStaging staging, ReleaseManifest manifest, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ReleaseCommitResult> CommitAsync(ReleaseCaptureStaging staging, string manifestDigest, CancellationToken cancellationToken = default) => Task.FromResult(new ReleaseCommitResult("/memory", true));
        public Task DiscardAsync(ReleaseCaptureStaging staging, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DiscardCommittedAsync(Guid applicationId, string sequenceNumber, string manifestDigest, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ReleaseManifest?> ReadManifestAsync(Guid applicationId, string sequenceNumber, string manifestDigest, CancellationToken cancellationToken = default) => Task.FromResult<ReleaseManifest?>(null);
        public Task<ReleaseFileContent> OpenFileAsync(Guid applicationId, string sequenceNumber, string manifestDigest, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
