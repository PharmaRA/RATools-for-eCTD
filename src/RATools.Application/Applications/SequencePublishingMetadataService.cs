using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Application.Standards;
using RATools.Domain.Applications;
using RATools.Application.Workspaces;

namespace RATools.Application.Applications;

public sealed class SequencePublishingMetadataService(
    IApplicationRepository applicationRepository,
    IStandardsProfileProvider standardsProfileProvider,
    WorkspaceMutationCoordinator mutations) : ISequencePublishingMetadataService
{
    public async Task<SequencePublishingMetadataDto?> GetAsync(
        Guid applicationId,
        string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetAsync(applicationId, cancellationToken);
        var sequence = application?.Sequences.SingleOrDefault(x => x.SequenceNumber == sequenceNumber);
        return application is null || sequence is null
            ? null
            : ToDto(application, sequence);
    }

    public async Task<SequencePublishingMetadataDto?> UpdateAsync(
        Guid applicationId,
        string sequenceNumber,
        UpdateSequencePublishingMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var mutation = await mutations.AcquireAsync(applicationId, sequenceNumber, request.ExpectedRevision, cancellationToken);
        var application = await applicationRepository.GetAsync(applicationId, cancellationToken);
        var sequence = application?.Sequences.SingleOrDefault(x => x.SequenceNumber == sequenceNumber);
        if (application is null || sequence is null)
        {
            return null;
        }

        var metadata = SequencePublishingMetadata.Create(
            request.ApplicationType,
            request.SubmissionType,
            request.SubmissionSubtype,
            request.SequenceDescription,
            request.ApplicantName,
            request.FormType,
            request.ApplicantContactName,
            request.ApplicantContactType,
            request.Telephone,
            request.TelephoneNumberType,
            request.Email);

        var previous = sequence.PublishingMetadata;
        try
        {
            await mutation.CommitAsync(async ct =>
            {
                sequence.RevisePublishingMetadata(metadata);
                await applicationRepository.UpdateAsync(application, ct);
            }, cancellationToken);
        }
        catch
        {
            sequence.RestorePublishingMetadata(previous);
            throw;
        }
        return ToDto(application, sequence) with { WorkspaceRevision = mutation.Revision };
    }

    private SequencePublishingMetadataDto ToDto(SubmissionApplication application, SubmissionSequence sequence)
    {
        var standardsProfile = standardsProfileProvider.GetProfile(application.EctdTemplateKey);
        var metadata = sequence.PublishingMetadata;

        return new SequencePublishingMetadataDto(
            application.Id,
            sequence.SequenceNumber,
            standardsProfile.DisplayName,
            metadata?.ApplicationType,
            metadata?.SubmissionType ?? sequence.SubmissionType,
            metadata?.SubmissionSubtype,
            metadata?.SequenceDescription ?? sequence.Description,
            metadata?.ApplicantName ?? application.SponsorName,
            metadata?.FormType,
            metadata?.ApplicantContactName,
            metadata?.ApplicantContactType,
            metadata?.Telephone,
            metadata?.TelephoneNumberType,
            metadata?.Email,
            sequence.WorkspaceRevision);
    }
}
