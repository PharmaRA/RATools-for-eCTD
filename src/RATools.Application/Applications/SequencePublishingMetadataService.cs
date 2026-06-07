using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Application.Standards;
using RATools.Domain.Applications;

namespace RATools.Application.Applications;

public sealed class SequencePublishingMetadataService(
    IApplicationRepository applicationRepository,
    IStandardsProfileProvider standardsProfileProvider) : ISequencePublishingMetadataService
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
            request.FormType);

        sequence.RevisePublishingMetadata(metadata);
        await applicationRepository.UpdateAsync(application, cancellationToken);
        return ToDto(application, sequence);
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
            metadata?.FormType);
    }
}
