using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Domain.Applications;

namespace RATools.Application.Applications;

public sealed class ApplicationService(
    IApplicationRepository repository,
    IDocumentPlacementRepository placementRepository,
    IPublishJobRepository publishJobRepository) : IApplicationService
{
    public async Task<ApplicationDto> CreateAsync(CreateApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var application = new SubmissionApplication(request.ApplicationNumber, request.Region, request.SponsorName);
        await repository.AddAsync(application, cancellationToken);
        return application.ToDto();
    }

    public async Task<IReadOnlyCollection<ApplicationDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var applications = await repository.ListAsync(cancellationToken);
        return applications.Select(x => x.ToDto()).ToArray();
    }

    public async Task<ApplicationDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await repository.GetAsync(id, cancellationToken);
        return application?.ToDto();
    }

    public async Task<ApplicationDto?> CreateSequenceAsync(Guid applicationId, CreateSequenceRequest request, CancellationToken cancellationToken = default)
    {
        var application = await repository.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return null;
        }

        application.CreateSequence(request.SequenceNumber, request.SubmissionType, request.Description);
        await repository.UpdateAsync(application, cancellationToken);
        return application.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var application = await repository.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return false;
        }

        var placements = await placementRepository.ListByApplicationAsync(applicationId, cancellationToken);
        if (placements.Count > 0)
        {
            throw new ApplicationDeleteConflictException($"Application {applicationId} cannot be deleted because document placements exist.");
        }

        var publishJobs = await publishJobRepository.QueryHistoryAsync(
            new PublishJobHistoryQuery(applicationId, null, null, null, null, 1, 1),
            cancellationToken);

        if (publishJobs.TotalCount > 0)
        {
            throw new ApplicationDeleteConflictException($"Application {applicationId} cannot be deleted because publish jobs exist.");
        }

        await repository.DeleteAsync(applicationId, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteSequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        var application = await repository.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return false;
        }

        var placements = await placementRepository.ListBySequenceAsync(applicationId, sequenceNumber, cancellationToken);
        if (placements.Count > 0)
        {
            throw new SequenceDeleteConflictException($"Sequence {sequenceNumber} cannot be deleted because document placements exist.");
        }

        var publishJobs = await publishJobRepository.QueryHistoryAsync(
            new PublishJobHistoryQuery(applicationId, sequenceNumber, null, null, null, 1, 1),
            cancellationToken);

        if (publishJobs.TotalCount > 0)
        {
            throw new SequenceDeleteConflictException($"Sequence {sequenceNumber} cannot be deleted because publish jobs exist.");
        }

        var removed = application.RemoveSequence(sequenceNumber);
        if (!removed)
        {
            return false;
        }

        await repository.UpdateAsync(application, cancellationToken);
        return true;
    }
}

internal static class ApplicationMapping
{
    public static ApplicationDto ToDto(this SubmissionApplication application)
    {
        return new ApplicationDto(
            application.Id,
            application.ApplicationNumber,
            application.Region,
            application.SponsorName,
            application.CreatedUtc,
            application.Sequences
                .Select(x => new SequenceDto(x.SequenceNumber, x.SubmissionType, x.Description, x.CreatedUtc))
                .ToArray());
    }
}
