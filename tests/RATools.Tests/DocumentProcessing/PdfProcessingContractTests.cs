using System.Text;
using RATools.Application.DocumentProcessing;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfProcessingContractTests
{
    [Fact]
    public void Plan_BindsImmutableSourceAndCanonicalParametersDigest()
    {
        var source = DocumentVersion.CreateSource(
            Guid.NewGuid(), Guid.NewGuid(), "0001", "submission.pdf", "application/pdf", 10,
            new string('a', 64), new string('b', 32), "C:/work/0001/submission.pdf", DateTime.UtcNow);
        var parameters = new PdfProcessingParameters(
            "UseOutlines",
            [new PdfBookmarkEdit("章节一", 1, null, 0)],
            [new PdfLinkEdit(1, "#章节一", PdfLinkTargetKind.NamedDestination, "page[1]/annotation[1]", "#overview")]);

        var applicationId = source.ApplicationId;
        var placementId = Guid.NewGuid();
        var plan = PdfProcessingPlan.Create(source, applicationId, "0001", placementId, 7, parameters, "qpdf-12.4.1");

        Assert.Equal(source.Id, plan.SourceVersionId);
        Assert.Equal(source.Sha256, plan.SourceSha256);
        Assert.Equal(source.Md5, plan.SourceMd5);
        Assert.Equal(placementId, plan.PlacementId);
        Assert.Equal(7, plan.ExpectedWorkspaceRevision);
        Assert.Equal("qpdf-12.4.1", plan.EngineVersion);
        plan.Verify();
        Assert.Contains("章节一", plan.ParametersJson, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanDigest_BindsEveryWorkspaceAndSourceIdentity()
    {
        var source = DocumentVersion.CreateSource(Guid.NewGuid(), Guid.NewGuid(), "0001", "submission.pdf", "application/pdf", 10,
            new string('a', 64), new string('b', 32), "C:/work/0001/submission.pdf", DateTime.UtcNow);
        var otherSource = DocumentVersion.CreateSource(Guid.NewGuid(), source.ApplicationId, "0001", "submission.pdf", "application/pdf", 10,
            source.Sha256, source.Md5, "C:/work/0001/submission-2.pdf", DateTime.UtcNow);
        var applicationId = source.ApplicationId;
        var placementId = Guid.NewGuid();
        var parameters = new PdfProcessingParameters(null, [], []);
        var plan = PdfProcessingPlan.Create(source, applicationId, "0001", placementId, 0, parameters, "qpdf-12.4.1");
        var variants = new[]
        {
            PdfProcessingPlan.Create(source, applicationId, "0001", Guid.NewGuid(), 0, parameters, "qpdf-12.4.1").PlanDigest,
            PdfProcessingPlan.Create(otherSource, applicationId, "0001", placementId, 0, parameters, "qpdf-12.4.1").PlanDigest,
            PdfProcessingPlan.Create(source, applicationId, "0001", placementId, 1, parameters, "qpdf-12.4.1").PlanDigest
        };
        Assert.All(variants, digest => Assert.NotEqual(plan.PlanDigest, digest));
        Assert.Throws<ArgumentException>(() => PdfProcessingPlan.Create(source, Guid.NewGuid(), "0001", placementId, 0, parameters, "qpdf-12.4.1"));
        Assert.Throws<ArgumentException>(() => PdfProcessingPlan.Create(source, applicationId, "0002", placementId, 0, parameters, "qpdf-12.4.1"));
    }

    [Fact]
    public void Plan_RejectsEncryptedOrSignedSourceFacts()
    {
        var source = DocumentVersion.CreateSource(
            Guid.NewGuid(), Guid.NewGuid(), "0001", "submission.pdf", "application/pdf", 10,
            new string('a', 64), new string('b', 32), "C:/work/0001/submission.pdf", DateTime.UtcNow);

        Assert.Throws<PdfProcessingRejectedException>(() => PdfProcessingPlan.Create(source, source.ApplicationId, "0001", Guid.NewGuid(), 0,
            new PdfProcessingParameters(null, [], []), "qpdf-12.4.1",
            new PdfSourceFacts(true, false, 1, true)));
        Assert.Throws<PdfProcessingRejectedException>(() => PdfProcessingPlan.Create(source, source.ApplicationId, "0001", Guid.NewGuid(), 0,
            new PdfProcessingParameters(null, [], []), "qpdf-12.4.1",
            new PdfSourceFacts(false, true, 1, true)));
    }

    [Fact]
    public async Task InMemoryStore_EnforcesCompleteRequestIdempotencyAndLeaseFence()
    {
        var store = new InMemoryDocumentProcessingStore();
        var plan = NewPlan();
        await store.AddPlanAsync(plan);
        var job = PdfProcessingJob.Create(plan, "request-key");

        var first = await store.EnqueueAsync(job);
        var replay = await store.EnqueueAsync(PdfProcessingJob.Create(plan, "request-key"));
        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(job.Id, replay.Job.Id);

        var otherPlan = NewPlan();
        await store.AddPlanAsync(otherPlan);
        await Assert.ThrowsAsync<PdfProcessingIdempotencyConflictException>(() => store.EnqueueAsync(
            PdfProcessingJob.Create(otherPlan, "request-key")));

        var lease = Assert.IsType<PdfProcessingLease>(await store.ClaimAsync("worker-a", DateTime.UtcNow, TimeSpan.FromMinutes(1)));
        Assert.False(await store.HeartbeatAsync(job.Id, Guid.NewGuid(), lease.Owner, DateTime.UtcNow, TimeSpan.FromMinutes(1), "running"));
        Assert.True(await store.HeartbeatAsync(job.Id, lease.Token, lease.Owner, DateTime.UtcNow, TimeSpan.FromMinutes(1), "running"));
    }

    private static PdfProcessingPlan NewPlan()
    {
        var source = DocumentVersion.CreateSource(
            Guid.NewGuid(), Guid.NewGuid(), "0001", "submission.pdf", "application/pdf", 10,
            new string('a', 64), new string('b', 32), "C:/work/0001/submission.pdf", DateTime.UtcNow);
        return PdfProcessingPlan.Create(source, source.ApplicationId, "0001", Guid.NewGuid(), 0,
            new PdfProcessingParameters(null, [], []), "qpdf-12.4.1");
    }

    [Fact]
    public void Plan_RejectsAlteredDocumentIdentityAndPageCount()
    {
        var plan = NewPlan();
        Assert.Throws<InvalidDataException>(() => (plan with { SourceDocumentId = Guid.NewGuid() }).Verify());
        Assert.Throws<InvalidDataException>(() => (plan with { SourcePageCount = 99 }).Verify());
    }
}
