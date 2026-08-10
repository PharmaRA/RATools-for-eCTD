using Microsoft.EntityFrameworkCore;
using RATools.Application.Applications.EctdTemplates;
using RATools.Domain.Applications;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence;

public sealed class EfCoreApplicationRepositoryTests
{
    [Fact]
    public async Task UpdateAsync_PersistsWorkingDirectoryPath()
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseInMemoryDatabase($"ratools-app-repo-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new RAToolsDbContext(options);
        var repository = new EfCoreApplicationRepository(dbContext);
        var applicationId = Guid.NewGuid();
        var createdUtc = DateTime.UtcNow;

        var original = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            createdUtc,
            [],
            Path.Combine("C:\\workspace", "app-001"),
            EctdTemplateRegistry.DefaultTemplateKey);

        await repository.AddAsync(original);

        var updated = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            createdUtc,
            [],
            Path.Combine("D:\\exports", "app-001"),
            EctdTemplateRegistry.DefaultTemplateKey);

        await repository.UpdateAsync(updated);

        var persisted = await repository.GetAsync(applicationId);

        Assert.NotNull(persisted);
        Assert.Equal(Path.Combine("D:\\exports", "app-001"), persisted.WorkingDirectoryPath);
    }

    [Fact]
    public async Task UpdateAsync_PersistsSequencePublishingMetadata()
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseInMemoryDatabase($"ratools-app-repo-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new RAToolsDbContext(options);
        var repository = new EfCoreApplicationRepository(dbContext);
        var applicationId = Guid.NewGuid();
        var createdUtc = DateTime.UtcNow;
        var sequence = SubmissionSequence.Rehydrate("0001", "amendment", "Amendment", DateTime.UtcNow);
        var original = SubmissionApplication.Rehydrate(
            applicationId,
            "app-001",
            "US",
            "Sponsor",
            createdUtc,
            [sequence],
            Path.Combine("C:\\workspace", "app-001"),
            EctdTemplateRegistry.DefaultTemplateKey);

        await repository.AddAsync(original);

        sequence.RevisePublishingMetadata(SequencePublishingMetadata.Create(
            "IND",
            "protocol-amendment",
            "safety",
            "Updated sequence description",
            "Updated Applicant",
            "form-1571",
            "Jane Regulatory",
            "regulatory",
            "301-555-0100",
            "office",
            "jane.regulatory@example.test"));
        await repository.UpdateAsync(original);

        var persisted = await repository.GetAsync(applicationId);

        Assert.NotNull(persisted);
        var persistedSequence = Assert.Single(persisted!.Sequences);
        Assert.NotNull(persistedSequence.PublishingMetadata);
        Assert.Equal("IND", persistedSequence.PublishingMetadata!.ApplicationType);
        Assert.Equal("protocol-amendment", persistedSequence.PublishingMetadata.SubmissionType);
        Assert.Equal("safety", persistedSequence.PublishingMetadata.SubmissionSubtype);
        Assert.Equal("Updated sequence description", persistedSequence.PublishingMetadata.SequenceDescription);
        Assert.Equal("Updated Applicant", persistedSequence.PublishingMetadata.ApplicantName);
        Assert.Equal("form-1571", persistedSequence.PublishingMetadata.FormType);
        Assert.Equal("Jane Regulatory", persistedSequence.PublishingMetadata.ApplicantContactName);
        Assert.Equal("regulatory", persistedSequence.PublishingMetadata.ApplicantContactType);
        Assert.Equal("301-555-0100", persistedSequence.PublishingMetadata.Telephone);
        Assert.Equal("office", persistedSequence.PublishingMetadata.TelephoneNumberType);
        Assert.Equal("jane.regulatory@example.test", persistedSequence.PublishingMetadata.Email);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("C:\\escape")]
    [InlineData("CON")]
    public async Task GetAsync_RejectsUnsafeLegacyApplicationNumberBeforeDerivingFallbackWorkspacePath(
        string applicationNumber)
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseInMemoryDatabase($"ratools-app-repo-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new RAToolsDbContext(options);
        var applicationId = Guid.NewGuid();
        dbContext.Applications.Add(new ApplicationRecord
        {
            Id = applicationId,
            ApplicationNumber = applicationNumber,
            Region = "US",
            SponsorName = "Sponsor",
            EctdTemplateKey = EctdTemplateRegistry.DefaultTemplateKey,
            WorkingDirectoryPath = string.Empty,
            CreatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var repository = new EfCoreApplicationRepository(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAsync(applicationId));
    }
}
