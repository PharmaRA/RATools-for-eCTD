using Microsoft.Extensions.DependencyInjection;
using RATools.Application;
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
            new EctdUsRegionalMetadata(
                "ANDA123456",
                "Acme Pharma",
                "Initial sequence",
                "Jane Regulatory",
                "regulatory",
                "301-555-0100",
                "office",
                "jane.regulatory@example.test",
                "anda",
                "original-application",
                "initial",
                "356h"),
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
        Assert.Equal("ANDA123456", package.UsRegional.ApplicantId);
        Assert.Equal("Acme Pharma", package.UsRegional.CompanyName);
        Assert.Equal("Initial sequence", package.UsRegional.SubmissionDescription);
        Assert.Equal("Jane Regulatory", package.UsRegional.ApplicantContactName);
        Assert.Equal("regulatory", package.UsRegional.ApplicantContactType);
        Assert.Equal("301-555-0100", package.UsRegional.Telephone);
        Assert.Equal("office", package.UsRegional.TelephoneNumberType);
        Assert.Equal("jane.regulatory@example.test", package.UsRegional.Email);
        Assert.Equal("anda", package.UsRegional.ApplicationType);
        Assert.Equal("original-application", package.UsRegional.SubmissionType);
        Assert.Equal("initial", package.UsRegional.SubmissionSubtype);
        Assert.Equal("356h", package.UsRegional.FormType);
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
        Assert.Equal("ANDA123456", package.UsRegional.ApplicantId);
        Assert.Equal("Acme Pharma", package.UsRegional.CompanyName);
        Assert.Equal("Initial sequence", package.UsRegional.SubmissionDescription);
        Assert.Equal(string.Empty, package.UsRegional.ApplicantContactName);
        Assert.Equal(string.Empty, package.UsRegional.ApplicantContactType);
        Assert.Equal(string.Empty, package.UsRegional.Telephone);
        Assert.Equal(string.Empty, package.UsRegional.TelephoneNumberType);
        Assert.Equal(string.Empty, package.UsRegional.Email);
        Assert.Equal("anda", package.UsRegional.ApplicationType);
        Assert.Equal("original-application", package.UsRegional.SubmissionType);
        Assert.Equal(string.Empty, package.UsRegional.SubmissionSubtype);
        Assert.Null(package.UsRegional.FormType);
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
        Assert.Equal("Regulatory Applicant LLC", package.UsRegional.CompanyName);
        Assert.Equal("Safety update", package.UsRegional.SubmissionDescription);
        Assert.Equal("anda", package.UsRegional.ApplicationType);
        Assert.Equal("supplement", package.UsRegional.SubmissionType);
        Assert.Equal("efficacy", package.UsRegional.SubmissionSubtype);
        Assert.Equal("356h", package.UsRegional.FormType);
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

    [Fact]
    public async Task BuildAsync_CreatesDeterministicLeavesAndSplitsModules()
    {
        var applicationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var oldTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newTime = oldTime.AddMinutes(1);
        var application = CreateApplication(applicationId, "0001");
        var module1Document = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "cover.pdf", "C:/workspace/ANDA123456/0001/m1/us/cover.pdf", 10, "sha-cover");
        var module3Document = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "quality.pdf", "C:/workspace/ANDA123456/0001/m3/32-body-data/quality.pdf", 20, "sha-quality");
        var module3PlacementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var module1PlacementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var placements = new[]
        {
            DocumentPlacement.Rehydrate(module3PlacementId, module3Document.Id, applicationId, "0001", "m3.2", DocumentPlacementOperation.New, null, null, newTime),
            DocumentPlacement.Rehydrate(module1PlacementId, module1Document.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.New, "Cover Letter", null, oldTime)
        };
        var builder = CreateBuilder(application, placements, [module1Document, module3Document]);

        var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001"));

        var module1Leaf = Assert.Single(package.Module1Leaves);
        Assert.Equal(module1PlacementId, module1Leaf.PlacementId);
        Assert.Equal("leaf-bbbbbbbb000000000000000000000001", module1Leaf.LeafId);
        Assert.Equal("m1", module1Leaf.Module);
        Assert.Equal("new", module1Leaf.Operation);
        Assert.Equal("Cover Letter", module1Leaf.Title);
        Assert.Equal("m1/us/cover.pdf", module1Leaf.Href);
        Assert.Equal("cover.pdf", module1Leaf.FileName);
        Assert.Equal("application/pdf", module1Leaf.MediaType);
        Assert.Equal(module1Document.StoragePath, module1Leaf.SourcePath);
        Assert.Equal(10, module1Leaf.FileSize);
        Assert.Equal("sha-cover", module1Leaf.Sha256);
        Assert.Null(module1Leaf.Lifecycle);

        var module3Leaf = Assert.Single(package.IchBackboneLeaves);
        Assert.Equal(module3PlacementId, module3Leaf.PlacementId);
        Assert.Equal("m3", module3Leaf.Module);
        Assert.Equal("quality.pdf", module3Leaf.Title);
        Assert.Equal("m3/32-body-data/quality.pdf", module3Leaf.Href);
    }

    [Fact]
    public async Task BuildAsync_CreatesPublishedFileInventoryWithoutDuplicates()
    {
        var applicationId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var application = CreateApplication(applicationId, "0001");
        var document = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "same.pdf", "C:/workspace/ANDA123456/0001/m1/us/same.pdf", 30, "sha-same");
        var placements = new[]
        {
            DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.New, "First", null, DateTime.UtcNow),
            DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "m1.2", DocumentPlacementOperation.New, "Second", null, DateTime.UtcNow.AddMinutes(1))
        };
        var builder = CreateBuilder(application, placements, [document]);

        var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001"));

        var file = Assert.Single(package.PublishedFiles);
        Assert.Equal(document.Id, file.DocumentId);
        Assert.Equal("m1/us/same.pdf", file.Href);
        Assert.Equal("same.pdf", file.FileName);
        Assert.Equal(30, file.FileSize);
        Assert.Equal("sha-same", file.Sha256);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenPlacementDocumentIsMissing()
    {
        var applicationId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var missingDocumentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
        var placementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004");
        var application = CreateApplication(applicationId, "0001");
        var placement = DocumentPlacement.Rehydrate(placementId, missingDocumentId, applicationId, "0001", "m1.1", DocumentPlacementOperation.New, "Missing", null, DateTime.UtcNow);
        var builder = CreateBuilder(application, [placement], []);

        var exception = await Assert.ThrowsAsync<EctdPackageDocumentNotFoundException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

        Assert.Equal(applicationId, exception.ApplicationId);
        Assert.Equal("0001", exception.SequenceNumber);
        Assert.Equal(placementId, exception.PlacementId);
        Assert.Equal(missingDocumentId, exception.DocumentId);
    }

    [Fact]
    public async Task BuildAsync_ResolvesLifecycleModifiedFileHrefForReplaceOperation()
    {
        var applicationId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var application = CreateApplication(applicationId, "0000", "0001");
        var historicalDocument = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"), "old.pdf", "C:/workspace/ANDA123456/0000/m1/us/old.pdf", 40, "sha-old");
        var currentDocument = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006"), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
        var historicalPlacementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005");
        var historicalPlacement = DocumentPlacement.Rehydrate(historicalPlacementId, historicalDocument.Id, applicationId, "0000", "m1.1", DocumentPlacementOperation.New, "Old", null, DateTime.UtcNow);
        var currentPlacement = DocumentPlacement.Rehydrate(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000006"), currentDocument.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", historicalPlacementId, DateTime.UtcNow.AddDays(1));
        var builder = CreateBuilder(application, [historicalPlacement, currentPlacement], [historicalDocument, currentDocument]);

        var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001"));

        var leaf = Assert.Single(package.Module1Leaves);
        Assert.Equal("replace", leaf.Operation);
        Assert.NotNull(leaf.Lifecycle);
        Assert.Equal(historicalPlacementId, leaf.Lifecycle.TargetPlacementId);
        Assert.Equal(historicalDocument.Id, leaf.Lifecycle.TargetDocumentId);
        Assert.Equal("0000", leaf.Lifecycle.TargetSequenceNumber);
        Assert.Equal("m1/us/old.pdf", leaf.Lifecycle.ModifiedFileHref);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenLifecycleTargetIsMissing()
    {
        var applicationId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var application = CreateApplication(applicationId, "0001");
        var document = CreateDocument(Guid.NewGuid(), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
        var placement = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", null, DateTime.UtcNow);
        var builder = CreateBuilder(application, [placement], [document]);

        var exception = await Assert.ThrowsAsync<EctdPackageLifecycleTargetException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

        Assert.Equal(placement.Id, exception.PlacementId);
        Assert.Null(exception.TargetPlacementId);
    }

    [Theory]
    [InlineData("0001", "target sequence is not earlier than current sequence")]
    [InlineData("0002", "target sequence is not earlier than current sequence")]
    public async Task BuildAsync_ThrowsWhenLifecycleTargetSequenceIsNotEarlier(string targetSequenceNumber, string expectedReason)
    {
        var applicationId = Guid.Parse("abababab-abab-abab-abab-abababababab");
        var application = CreateApplication(applicationId, "0001", "0002");
        var targetDocument = CreateDocument(Guid.NewGuid(), "old.pdf", $"C:/workspace/ANDA123456/{targetSequenceNumber}/m1/us/old.pdf", 40, "sha-old");
        var currentDocument = CreateDocument(Guid.NewGuid(), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
        var targetPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), targetDocument.Id, applicationId, targetSequenceNumber, "m1.1", DocumentPlacementOperation.New, "Old", null, DateTime.UtcNow);
        var currentPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), currentDocument.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", targetPlacement.Id, DateTime.UtcNow.AddDays(1));
        var builder = CreateBuilder(application, [targetPlacement, currentPlacement], [targetDocument, currentDocument]);

        var exception = await Assert.ThrowsAsync<EctdPackageLifecycleTargetException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

        Assert.Equal(expectedReason, exception.Reason);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenLifecycleTargetIsInDifferentSection()
    {
        var applicationId = Guid.Parse("bcbcbcbc-bcbc-bcbc-bcbc-bcbcbcbcbcbc");
        var application = CreateApplication(applicationId, "0000", "0001");
        var targetDocument = CreateDocument(Guid.NewGuid(), "old.pdf", "C:/workspace/ANDA123456/0000/m1/us/old.pdf", 40, "sha-old");
        var currentDocument = CreateDocument(Guid.NewGuid(), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
        var targetPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), targetDocument.Id, applicationId, "0000", "m1.2", DocumentPlacementOperation.New, "Old", null, DateTime.UtcNow);
        var currentPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), currentDocument.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", targetPlacement.Id, DateTime.UtcNow.AddDays(1));
        var builder = CreateBuilder(application, [targetPlacement, currentPlacement], [targetDocument, currentDocument]);

        var exception = await Assert.ThrowsAsync<EctdPackageLifecycleTargetException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

        Assert.Equal("target placement is in a different CTD section", exception.Reason);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenLifecycleTargetDocumentIsMissing()
    {
        var applicationId = Guid.Parse("cacacaca-caca-caca-caca-cacacacacaca");
        var application = CreateApplication(applicationId, "0000", "0001");
        var targetDocumentId = Guid.NewGuid();
        var currentDocument = CreateDocument(Guid.NewGuid(), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
        var targetPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), targetDocumentId, applicationId, "0000", "m1.1", DocumentPlacementOperation.New, "Old", null, DateTime.UtcNow);
        var currentPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), currentDocument.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", targetPlacement.Id, DateTime.UtcNow.AddDays(1));
        var builder = CreateBuilder(application, [targetPlacement, currentPlacement], [currentDocument]);

        var exception = await Assert.ThrowsAsync<EctdPackageLifecycleTargetException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

        Assert.Equal("target document was not found", exception.Reason);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenCtdSectionModuleIsUnsupported()
    {
        var applicationId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");
        var application = CreateApplication(applicationId, "0001");
        var document = CreateDocument(Guid.NewGuid(), "bad.pdf", "C:/workspace/ANDA123456/0001/x1/bad.pdf", 10, "sha-bad");
        var placement = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "x1.1", DocumentPlacementOperation.New, "Bad", null, DateTime.UtcNow);
        var builder = CreateBuilder(application, [placement], [document]);

        var exception = await Assert.ThrowsAsync<EctdPackageInvalidSectionException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

        Assert.Equal("x1.1", exception.CtdSection);
    }

    [Fact]
    public async Task BuildAsync_ThrowsWhenOperationValueIsUnsupported()
    {
        var applicationId = Guid.Parse("dededede-dede-dede-dede-dededededede");
        var application = CreateApplication(applicationId, "0001");
        var document = CreateDocument(Guid.NewGuid(), "bad.pdf", "C:/workspace/ANDA123456/0001/m1/us/bad.pdf", 10, "sha-bad");
        var placement = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "m1.1", (DocumentPlacementOperation)999, "Bad", null, DateTime.UtcNow);
        var builder = CreateBuilder(application, [placement], [document]);

        var exception = await Assert.ThrowsAsync<EctdPackageUnsupportedOperationException>(
            () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

        Assert.Equal(999, exception.OperationValue);
    }

    [Fact]
    public void AddApplication_RegistersPackageModelBuilder()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IEctdPackageModelBuilder));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(EctdPackageModelBuilder), descriptor.ImplementationType);
    }

    private static SubmissionApplication CreateApplication(Guid applicationId, params string[] sequenceNumbers)
    {
        return SubmissionApplication.Rehydrate(
            applicationId,
            "ANDA123456",
            "US",
            "Acme Pharma",
            DateTime.UtcNow,
            sequenceNumbers.Select(x => SubmissionSequence.Rehydrate(x, "original-application", $"Sequence {x}", DateTime.UtcNow)).ToArray(),
            "C:/workspace/ANDA123456",
            "us-fda-ectd-322");
    }

    private static SubmissionDocument CreateDocument(Guid documentId, string fileName, string storagePath, long fileSize, string sha256)
    {
        return SubmissionDocument.Rehydrate(documentId, fileName, "application/pdf", fileSize, sha256, storagePath, DateTime.UtcNow);
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
