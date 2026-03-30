using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Domain.Applications;

namespace RATools.Application.Applications;

public sealed class ApplicationService(IApplicationRepository repository) : IApplicationService
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
