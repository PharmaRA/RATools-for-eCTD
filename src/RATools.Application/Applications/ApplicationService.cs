using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Applications.Requests;
using RATools.Domain.Applications;

namespace RATools.Application.Applications;

public sealed class ApplicationService(
    IApplicationRepository repository,
    IApplicationDeletionCoordinator deletionCoordinator,
    IWorkspacePathPolicy workspacePathPolicy,
    IApplicationWorkspaceService? workspaceService = null) : IApplicationService
{
    private readonly IApplicationWorkspaceService _workspaceService = workspaceService ?? new DefaultApplicationWorkspaceService();

    public async Task<ApplicationDto> CreateAsync(CreateApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var template = EctdTemplateRegistry.Resolve(request.EctdTemplateKey);
        var existingApplications = await repository.ListAsync(cancellationToken);
        if (existingApplications.Any(x => string.Equals(x.ApplicationNumber, request.ApplicationNumber, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ApplicationNumberAlreadyExistsException($"Application number '{request.ApplicationNumber}' already exists.");
        }

        var requestedWorkingDirectoryPath = Path.Combine(request.WorkingDirectoryParentPath, request.ApplicationNumber);
        var allowedWorkingDirectoryPath = Path.TrimEndingDirectorySeparator(workspacePathPolicy.EnsureAllowed(requestedWorkingDirectoryPath));
        var allowedParentPath = Path.GetDirectoryName(allowedWorkingDirectoryPath)
            ?? throw new InvalidOperationException($"Unable to derive a parent directory for '{allowedWorkingDirectoryPath}'.");
        var allowedApplicationNumber = Path.GetFileName(allowedWorkingDirectoryPath);

        var workingDirectoryPath = await _workspaceService.EnsureApplicationWorkingDirectoryAsync(
            allowedParentPath,
            allowedApplicationNumber,
            cancellationToken);

        var application = new SubmissionApplication(request.ApplicationNumber, template.Region, request.SponsorName, workingDirectoryPath, template.Key);
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
        await _workspaceService.EnsureSequenceWorkingDirectoryAsync(application.WorkingDirectoryPath, request.SequenceNumber, cancellationToken);
        await repository.UpdateAsync(application, cancellationToken);
        return application.ToDto();
    }

    public async Task<bool> DeleteAsync(
        Guid applicationId,
        ApplicationDeleteMode deleteMode = ApplicationDeleteMode.DatabaseOnly,
        CancellationToken cancellationToken = default)
    {
        var application = await repository.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return false;
        }

        await deletionCoordinator.DeleteApplicationAsync(application, deleteMode, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteSequenceAsync(
        Guid applicationId,
        string sequenceNumber,
        ApplicationDeleteMode deleteMode = ApplicationDeleteMode.DatabaseOnly,
        CancellationToken cancellationToken = default)
    {
        var application = await repository.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return false;
        }

        return await deletionCoordinator.DeleteSequenceAsync(application, sequenceNumber, deleteMode, cancellationToken);
    }
}

internal sealed class DefaultApplicationWorkspaceService : IApplicationWorkspaceService
{
    public Task<string> EnsureApplicationWorkingDirectoryAsync(string parentPath, string applicationNumber, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(parentPath, applicationNumber);
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }

    public Task<string> EnsureSequenceWorkingDirectoryAsync(string applicationWorkingDirectoryPath, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(applicationWorkingDirectoryPath, sequenceNumber);
        Directory.CreateDirectory(path);
        return Task.FromResult(path);
    }
}

internal static class ApplicationMapping
{
    public static ApplicationDto ToDto(this SubmissionApplication application)
    {
        var ectdTemplateDisplayName = ResolveTemplateDisplayName(application.EctdTemplateKey);

        return new ApplicationDto(
            application.Id,
            application.ApplicationNumber,
            application.SponsorName,
            application.WorkingDirectoryPath,
            application.CreatedUtc,
            application.EctdTemplateKey,
            ectdTemplateDisplayName,
            application.Sequences
                .Select(x => new SequenceDto(
                    x.SequenceNumber,
                    x.SubmissionType,
                    x.Description,
                    Path.Combine(application.WorkingDirectoryPath, x.SequenceNumber),
                    x.CreatedUtc))
                .ToArray());
    }

    private static string ResolveTemplateDisplayName(string ectdTemplateKey)
    {
        try
        {
            return EctdTemplateRegistry.Resolve(ectdTemplateKey).DisplayName;
        }
        catch (EctdTemplateNotFoundException)
        {
            return $"Unknown Template ({ectdTemplateKey})";
        }
    }
}
