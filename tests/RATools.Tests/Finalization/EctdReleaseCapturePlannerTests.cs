using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Application.Documents;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Standards;
using RATools.Domain.Documents;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Finalization;

// P4-03: the planner derives the deliverable from one consistent workspace read, pins the
// standards assets and records exactly the validation facts the archived run bound.
public sealed class EctdReleaseCapturePlannerTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PlanCapturesTheDeliverableStandardsAndReportWithTheRunsOwnFacts()
    {
        using var fixture = new EctdWorkspaceFixture("us-fda-ectd-3.2.2");
        await fixture.AddSequenceAsync("0000");
        await fixture.AddDocumentAsync("0000", "m2.2", "intro.pdf", "%PDF-1.4 minimal");
        var run = CompletedRun(fixture);
        var planner = Planner(fixture);

        var preparation = await planner.PlanAsync(Request(fixture, run), run, StoredRequest(run));

        // The payload carries both backbones, the document, the regional DTDs and the
        // generated index-md5.txt; nothing is read from a path outside the workspace.
        Assert.Contains(preparation.Capture.Files, file => file.RelativePath == "payload/0000/index.xml");
        Assert.Contains(preparation.Capture.Files, file => file.RelativePath == "payload/0000/m1/us/us-regional.xml");
        // The document path is derived from the workspace section, so match its file name
        // and digest rather than hard-coding the template's folder wording.
        Assert.Contains(preparation.Capture.Files, file => file.RelativePath.EndsWith("/intro.pdf", StringComparison.Ordinal));
        Assert.Contains(preparation.Capture.Files, file => file.RelativePath == "payload/0000/util/dtd/ich-ectd-3-2.dtd");
        Assert.Contains(preparation.Capture.Files, file => file.RelativePath == "payload/0000/index-md5.txt");
        Assert.Equal(2, preparation.Assets.Count);
        Assert.All(preparation.Assets, asset => Assert.Contains(preparation.Capture.Files,
            file => file.RelativePath == asset.Path && file.Sha256 == asset.Sha256));
        // The report is not part of the plan: the service appends the archived bytes itself.
        Assert.DoesNotContain(preparation.Capture.Files, file => file.Kind == ReleaseFileKind.ValidationReport);
        Assert.Equal(run.ReportSha256, preparation.Validation.ReportSha256);
        Assert.Equal(run.Binding.RulesDigest, preparation.Validation.RulesDigest);
        Assert.Equal(run.Binding.ProfileSnapshotId, preparation.Validation.StandardsProfileSnapshotId);
        Assert.Equal("3.2.2", preparation.Validation.IchEctdVersion);
        // The validated document map names exactly the delivered document bytes, using the
        // validation input's own logical path for the target sequence.
        var document = Assert.Single(preparation.ValidatedDocuments);
        Assert.StartsWith("0000/", document.Key, StringComparison.Ordinal);
        Assert.EndsWith("/intro.pdf", document.Key, StringComparison.Ordinal);
        Assert.Contains(preparation.Capture.Files,
            file => file.Sha256 == document.Value && file.Kind == ReleaseFileKind.Payload);
    }

    [Fact]
    public async Task PlanRefusesStandardsVersionsTheTemplateDoesNotDefine()
    {
        using var fixture = new EctdWorkspaceFixture("us-fda-ectd-3.2.2");
        await fixture.AddSequenceAsync("0000");
        await fixture.AddDocumentAsync("0000", "m2.2", "intro.pdf", "%PDF-1.4 minimal");
        var run = CompletedRun(fixture);
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            Planner(fixture).PlanAsync(Request(fixture, run) with { IchEctdVersion = "3.2.1" }, run, StoredRequest(run)));
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            Planner(fixture).PlanAsync(Request(fixture, run) with { RegionalModule1Version = "3.2" }, run, StoredRequest(run)));
    }

    private static EctdReleaseCapturePlanner Planner(EctdWorkspaceFixture fixture)
    {
        var boundaries = new DocumentStorageBoundary(fixture.PathPolicy);
        var writer = new UsRegionalBackboneWriter(new UsRegionalXmlWriter());
        return new EctdReleaseCapturePlanner(
            PackageTestServices.Create(fixture.Applications, fixture.Placements, fixture.Documents,
                new FdaEctd322StandardsProfileProvider(), boundaries),
            new IchIndexXmlWriter(), new RegionalBackboneWriterRegistry([writer]),
            new FdaEctd322StandardsProfileProvider());
    }

    private static PackageValidationRun CompletedRun(EctdWorkspaceFixture fixture)
    {
        var binding = new ValidationBinding(Digest("input"), Digest("baseline"), PackageValidationCatalog.UsProfile,
            PackageValidationCatalog.Current.Digest, "package-delivery-v3", PackageValidationMode.Development,
            new PackageReadLimits().Digest());
        return new PackageValidationRun(fixture.Application.Id, "0000", binding, "{}", Guid.NewGuid().ToString("N"), Now);
    }

    private static FinalizeSequenceRequest Request(EctdWorkspaceFixture fixture, PackageValidationRun run) =>
        new(fixture.Application.Id, "0000", 0, run.Id, PackageValidationCatalog.UsProfile, "3.2.2", "3.3",
            run.Binding.HistoryBaselineDigest, Guid.NewGuid().ToString("N"));

    private static StoredPackageValidationRequest StoredRequest(PackageValidationRun run)
    {
        var baseline = JsonSerializer.SerializeToElement(new
        {
            applicationId = run.ApplicationId.ToString("D"), targetSequence = "0000",
            digest = run.Binding.HistoryBaselineDigest, entries = Array.Empty<object>()
        });
        return new StoredPackageValidationRequest(1, Guid.NewGuid(), null, [], default, baseline,
            new PackageReadLimits());
    }

    private static string Digest(string seed) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
}
