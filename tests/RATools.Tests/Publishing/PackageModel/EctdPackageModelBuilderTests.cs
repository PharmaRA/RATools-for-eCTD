using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

namespace RATools.Tests.Publishing.PackageModel;

public sealed class EctdPackageModelBuilderTests
{
    [Fact]
    public void PackageRecords_ExposeExpectedImmutableContract()
    {
        var applicationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var placementId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var documentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var package = new EctdSequencePackage(
            applicationId,
            "ANDA123456",
            "0001",
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "3.2.2",
            "3.3",
            new EctdApplicationMetadata("ANDA123456", "Acme Pharma", "US", "us-fda-ectd-322", "anda"),
            new EctdSequenceMetadata("0001", "original-application", null, "Initial sequence", "Acme Pharma", "356h"),
            [
                new EctdLeaf(
                    placementId,
                    documentId,
                    "leaf-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "0001",
                    "m1.1",
                    "m1",
                    "new",
                    "Cover Letter",
                    "m1/us/cover.pdf",
                    "cover.pdf",
                    "application/pdf",
                    "C:/work/0001/m1/us/cover.pdf",
                    20,
                    "sha256",
                    null)
            ],
            [],
            [
                new EctdPublishedFile(
                    documentId,
                    "C:/work/0001/m1/us/cover.pdf",
                    "m1/us/cover.pdf",
                    "cover.pdf",
                    20,
                    "sha256")
            ]);

        Assert.Equal(applicationId, package.ApplicationId);
        Assert.Equal("3.2.2", package.IchEctdVersion);
        Assert.Single(package.Module1Leaves);
        Assert.Empty(package.IchBackboneLeaves);
        Assert.Single(package.PublishedFiles);
    }

    [Fact]
    public async Task BuildAsync_BuildsPackageMetadataWithSequenceFallbacks()
    {
        var applicationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "ANDA123456",
            "US",
            "Acme Pharma",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0001", "original-application", "Initial sequence", DateTime.UtcNow)],
            "C:/workspace/ANDA123456",
            "us-fda-ectd-322");
        var builder = CreateBuilder(application, [], []);

        var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001"));

        Assert.Equal(applicationId, package.ApplicationId);
        Assert.Equal("ANDA123456", package.ApplicationNumber);
        Assert.Equal("0001", package.SequenceNumber);
        Assert.Equal("FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3", package.StandardsProfile);
        Assert.Equal("3.2.2", package.IchEctdVersion);
        Assert.Equal("3.3", package.UsRegionalModule1Version);
        Assert.Equal("ANDA123456", package.Application.ApplicationNumber);
        Assert.Equal("Acme Pharma", package.Application.SponsorName);
        Assert.Equal("US", package.Application.Region);
        Assert.Equal("us-fda-ectd-322", package.Application.TemplateKey);
        Assert.Null(package.Application.ApplicationType);
        Assert.Equal("original-application", package.Sequence.SubmissionType);
        Assert.Null(package.Sequence.SubmissionSubtype);
        Assert.Equal("Initial sequence", package.Sequence.Description);
        Assert.Equal("Acme Pharma", package.Sequence.ApplicantName);
        Assert.Null(package.Sequence.FormType);
        Assert.Empty(package.Module1Leaves);
        Assert.Empty(package.IchBackboneLeaves);
        Assert.Empty(package.PublishedFiles);
    }

    [Fact]
    public async Task BuildAsync_UsesSequencePublishingMetadataWhenPresent()
    {
        var metadata = SequencePublishingMetadata.Create(
            "anda",
            "supplement",
            "efficacy",
            "Safety update",
            "Regulatory Applicant LLC",
            "356h");
        var applicationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "ANDA123456",
            "US",
            "Acme Pharma",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0002", "legacy-type", "Legacy description", DateTime.UtcNow, metadata)],
            "C:/workspace/ANDA123456",
            "us-fda-ectd-322");
        var builder = CreateBuilder(application, [], []);

        var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0002"));

        Assert.Equal("anda", package.Application.ApplicationType);
        Assert.Equal("supplement", package.Sequence.SubmissionType);
        Assert.Equal("efficacy", package.Sequence.SubmissionSubtype);
        Assert.Equal("Safety update", package.Sequence.Description);
        Assert.Equal("Regulatory Applicant LLC", package.Sequence.ApplicantName);
        Assert.Equal("356h", package.Sequence.FormType);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenApplicationIsMissing()
    {
        var builder = CreateBuilder(null, [], []);
        var applicationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var exception = await Assert.ThrowsAsync<EctdPackageApplicationNotFoundException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

        Assert.Equal(applicationId, exception.ApplicationId);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenSequenceIsMissing()
    {
        var applicationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "ANDA123456",
            "US",
            "Acme Pharma",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0001", "original-application", "Initial sequence", DateTime.UtcNow)],
            "C:/workspace/ANDA123456",
            "us-fda-ectd-322");
        var builder = CreateBuilder(application, [], []);

        var exception = await Assert.ThrowsAsync<EctdPackageSequenceNotFoundException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0009")));

        Assert.Equal(applicationId, exception.ApplicationId);
        Assert.Equal("0009", exception.SequenceNumber);
    }

    private static EctdPackageModelBuilder CreateBuilder(
        SubmissionApplication? application,
        IReadOnlyCollection<DocumentPlacement> placements,
        IReadOnlyCollection<SubmissionDocument> documents)
    {
        return new EctdPackageModelBuilder(
            new StubApplicationRepository(application),
            new StubDocumentPlacementRepository(placements),
            new StubDocumentRepository(documents),
            new StubStandardsProfileProvider());
    }

    private sealed class StubStandardsProfileProvider : IStandardsProfileProvider
    {
        public StandardsProfile GetProfile(string templateKey)
        {
            return new StandardsProfile(
                templateKey,
                "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
                "FDA CDER/CBER",
                "United States",
                "3.2.2",
                "3.3",
                "1.9",
                "4.5",
                [],
                []);
        }
    }

    private sealed class StubApplicationRepository(SubmissionApplication? application) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(application?.Id == id ? application : null);

        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>(application is null ? [] : [application]);
    }

    private sealed class StubDocumentRepository(IReadOnlyCollection<SubmissionDocument> documents) : IDocumentRepository
    {
        public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(documents.SingleOrDefault(x => x.Id == id));

        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(documents);
    }

    private sealed class StubDocumentPlacementRepository(IReadOnlyCollection<DocumentPlacement> placements) : IDocumentPlacementRepository
    {
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(placements.SingleOrDefault(x => x.Id == id));

        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(placements);

        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placements.Where(x => x.ApplicationId == applicationId).ToArray());

        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placements.Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber).ToArray());
    }
}
