using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using RATools.Application.Finalization;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.Validation;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Finalization;

public sealed class RegionalMetadataCaptureTests
{
    [Fact]
    public async Task BackboneGenerationValidatesWithTheRegionalRevisionsArchivedProfile()
    {
        var fixture = await CreateAsync();
        var archived = StandardsSnapshotService.Profile(await fixture.Snapshots.GetAsync(fixture.Revision.StandardsSnapshotId));
        var current = archived with { TechnicalConformanceGuideVersion = "later-current-guide" };
        var validator = new RecordingValidator(); var fileWriter = new RetainedOutput();
        var arguments = new object?[] { new CapturedPackage(fixture.Package), new IchIndexXmlWriter(),
            new RegionalBackboneWriterRegistry([new UsRegionalBackboneWriter(new UsRegionalXmlWriter())]), validator, new CurrentProfile(current), fileWriter, fixture.Snapshots };
        var constructor = typeof(BackboneService).GetConstructors().Single();
        var service = (BackboneService)constructor.Invoke(arguments.Take(constructor.GetParameters().Length).ToArray());
        await service.GenerateAsync(new(fixture.Revision.ApplicationId, "0001", Guid.NewGuid(), "report.json", "package.zip"));
        Assert.NotEmpty(validator.Profiles);
        Assert.All(validator.Profiles, profile => Assert.Equal("1.9", profile.TechnicalConformanceGuideVersion));
    }

    [Fact]
    public async Task CapturePlanRetainsTheExactRegionalRevisionAsManifestBoundNonPayloadEvidence()
    {
        var fixture = await CreateAsync();
        var plan = await fixture.Planner.PlanAsync(fixture.Request, fixture.Run, fixture.Stored);
        var file = Assert.Single(plan.Capture.Files, item => item.RelativePath == "regional-metadata.json");
        Assert.Equal("RegionalMetadata", file.Kind.ToString());
        Assert.NotNull(file.Content);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(file.Content)).ToLowerInvariant(), file.Sha256);
        var restored = JsonSerializer.Deserialize<RegionalMetadataRevision>(file.Content)!; restored.Verify();
        Assert.Equal(fixture.Revision.Id, restored.Id); Assert.Equal(fixture.Revision.Digest, restored.Digest);
        Assert.Equal(fixture.Revision.StandardsSnapshotId, restored.StandardsSnapshotId);
        Assert.Equal("Source applicant", restored.Content!.Us!.ApplicantName);
        Assert.DoesNotContain(plan.Capture.Files.Where(item => item.Kind.ToString() == "Payload"), item => item.RelativePath == file.RelativePath);
        var report = ReleaseCaptureFile.Generated("validation-report.json", System.Text.Encoding.UTF8.GetBytes("{}"), ReleaseFileKind.ValidationReport);
        var manifest = new ReleaseManifest(fixture.Revision.ApplicationId, "0001", 1, FinalizationService.ToolVersion,
            fixture.Run.Binding.InputManifestDigest, plan.HistoryBaseline, fixture.Run.Binding.HistoryBaselineDigest,
            plan.Validation with { ReportSha256 = report.Sha256 }, plan.Assets,
            plan.Capture.Files.Append(report).Select(item => new ReleaseManifestFile(item.RelativePath,item.Length,item.Sha256,item.Md5,item.Kind)),
            plan.Lifecycle,plan.Leaves,plan.Nodes);
        var reloaded = ReleaseManifest.FromJson(manifest.ToUtf8Json(),manifest.ApplicationId,manifest.SequenceNumber,manifest.Digest);
        Assert.Equal(file.Sha256,Assert.Single(reloaded.Files,item=>item.RelativePath=="regional-metadata.json").Sha256);
    }

    [Fact]
    public async Task RegionalRevisionCannotBeCapturedUnderAnotherValidationStandardsIdentity()
    {
        var fixture = await CreateAsync(mismatch: true);
        var error = await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => fixture.Planner.PlanAsync(fixture.Request, fixture.Run, fixture.Stored));
        Assert.Contains("regional", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SharedOnlyLegacyCaptureDoesNotInventRegionalEvidence()
    {
        var fixture = await CreateAsync(legacy: true);
        var plan = await fixture.Planner.PlanAsync(fixture.Request, fixture.Run, fixture.Stored);
        Assert.DoesNotContain(plan.Capture.Files, item => item.RelativePath == "regional-metadata.json");
        Assert.Contains(plan.Capture.Files, item => item.RelativePath == "payload/0001/m1/us/us-regional.xml");
    }

    private static async Task<(EctdReleaseCapturePlanner Planner, FinalizeSequenceRequest Request, PackageValidationRun Run,
        StoredPackageValidationRequest Stored, RegionalMetadataRevision Revision, EctdSequencePackage Package, StandardsSnapshotService Snapshots)> CreateAsync(bool mismatch = false, bool legacy = false)
    {
        var provider = new FdaEctd322StandardsProfileProvider();
        var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), provider);
        var snapshot = await snapshots.CaptureAsync("us-fda-ectd-3.2.2"); var profile = StandardsSnapshotService.Profile(snapshot);
        var appId = Guid.NewGuid();
        var us = JsonSerializer.Deserialize<UsRegionalMetadata>("""
            {"ApplicationIdentifier":"123456","ApplicationType":"nda","ApplicantName":"Source applicant","Contacts":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"Explicit contact","Type":"regulatory","Phones":[{"Id":"22222222-2222-2222-2222-222222222222","Number":"5550101","Type":"business"}],"Emails":[{"Id":"33333333-3333-3333-3333-333333333333","Value":"capture@example.test"}]}],"SubmissionType":"original-application","SubmissionSubtype":"application","FormType":null,"Description":"Source description","ApplicantIdentifier":"source-applicant-id","SubmissionIdentifier":"source-submission-id"}
            """)!;
        var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), appId, "0001", 1, 1, mismatch ? "sha256:" + new string('b', 64) : snapshot.Id,
            new(1, "US", Us: us), null, [], [], null, null, [new("us.applicantName", "UserEntered")], [], null, null, DateTime.UtcNow);
        var package = new EctdSequencePackage(appId, "folder-is-not-source", "0001", profile.DisplayName, "3.2.2", "3.3", profile.BackboneXml!,
            new("folder-is-not-source", "Legacy sponsor", "US", profile.TemplateKey, "nda"),
            new("0001", "original-application", "application", "Legacy description", "Legacy sponsor", null),
            new("legacy-applicant", "Legacy sponsor", "Legacy description", "Legacy contact", "regulatory", "5550100", "business", "legacy@example.test", "nda", "original-application", "application", null), [], [], [], WorkspaceRevision: 1);
        if (!legacy)
        {
            var json = JsonSerializer.SerializeToNode(package)!.AsObject(); json["RegionalMetadata"] = JsonSerializer.SerializeToNode(revision);
            package = json.Deserialize<EctdSequencePackage>()!;
        }
        var context = PackageStandardsContext.FromSnapshot(snapshot);
        var binding = new ValidationBinding(new string('a',64), new string('c',64), snapshot.RuleProfileAlias, context.Catalog.Digest,
            "regional-capture-test", PackageValidationMode.Development, new PackageReadLimits().Digest(), snapshot.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        var run = new PackageValidationRun(appId, "0001", binding, "{}", Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        var baseline = JsonSerializer.SerializeToElement(new { applicationId = appId.ToString("D"), targetSequence = "0001", digest = binding.HistoryBaselineDigest, entries = Array.Empty<object>() });
        var stored = new StoredPackageValidationRequest(1, Guid.NewGuid(), null, [], default, baseline, new PackageReadLimits());
        var request = new FinalizeSequenceRequest(appId, "0001", 1, run.Id, snapshot.RuleProfileAlias, "3.2.2", "3.3", binding.HistoryBaselineDigest, Guid.NewGuid().ToString("N"));
        var planner = new EctdReleaseCapturePlanner(new CapturedPackage(package), new IchIndexXmlWriter(),
            new RegionalBackboneWriterRegistry([new UsRegionalBackboneWriter(new UsRegionalXmlWriter())]), provider, snapshots);
        return (planner, request, run, stored, revision, package, snapshots);
    }
    private sealed class CapturedPackage(EctdSequencePackage package) : IStandardsBoundPackageModelBuilder
    {
        public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default) => Task.FromResult(package);
        public Task<EctdSequencePackage> BuildWithStandardsAsync(BuildEctdPackageRequest request, StandardsProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(package);
    }
    private sealed class CurrentProfile(StandardsProfile profile) : IStandardsProfileProvider
    { public StandardsProfile GetProfile(string templateKey) => profile; }
    private sealed class RecordingValidator : IEctdXmlValidator
    {
        internal List<StandardsProfile> Profiles { get; } = [];
        public void Validate(BackboneGeneratedFile file, StandardsProfile? standardsProfile = null)
        { Profiles.Add(standardsProfile!); new EctdXmlValidator().Validate(file, standardsProfile); }
    }
    private sealed class RetainedOutput : IBackboneFileWriter
    {
        public Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(Guid applicationId, string sequenceNumber, Guid publishJobId,
            IReadOnlyCollection<BackboneGeneratedFile> generatedFiles, string reportFileName, string packageFileName,
            IReadOnlyCollection<EctdPublishedFile> publishedFiles, IReadOnlyCollection<StandardsAsset>? standardsAssets = null, CancellationToken cancellationToken = default)
            => Task.FromResult(("retained/index.xml", "retained/report.json", "retained/package.zip"));
    }
}
