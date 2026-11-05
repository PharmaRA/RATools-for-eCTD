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
    WorkspaceMutationCoordinator mutations,
    RATools.Application.RegionalMetadata.RegionalMetadataService? regionalMetadata = null) : ISequencePublishingMetadataService
{
    public async Task<SequencePublishingMetadataDto?> GetAsync(
        Guid applicationId,
        string sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationRepository.GetAsync(applicationId, cancellationToken);
        var sequence = application?.Sequences.SingleOrDefault(x => x.SequenceNumber == sequenceNumber);
        var result = application is null || sequence is null
            ? null
            : ToDto(application, sequence);
        return result is null || regionalMetadata is null ? result : result with
        { Regional = RATools.Application.RegionalMetadata.RegionalMetadataRevisionDto.From(await regionalMetadata.GetScopedHeadAsync(applicationId, sequenceNumber, cancellationToken)) };
    }

    public async Task<SequencePublishingMetadataDto?> UpdateAsync(
        Guid applicationId,
        string sequenceNumber,
        UpdateSequencePublishingMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Regional is not null)
        {
            if (regionalMetadata is null) throw new InvalidOperationException("Regional metadata service is unavailable.");
            var mixedApplication = await applicationRepository.GetAsync(applicationId, cancellationToken);
            if (mixedApplication is null) return null;
            var capturedProfile = standardsProfileProvider.GetProfile(mixedApplication.EctdTemplateKey).DisplayName;
            var shared = SequencePublishingMetadata.Create(request.ApplicationType, request.SubmissionType, request.SubmissionSubtype,
                request.SequenceDescription, request.ApplicantName, request.FormType, request.ApplicantContactName, request.ApplicantContactType,
                request.Telephone, request.TelephoneNumberType, request.Email);
            var committed = await regionalMetadata.SaveMixedAsync(applicationId, sequenceNumber, request.ExpectedRevision, request.Regional, shared, cancellationToken);
            return new SequencePublishingMetadataDto(applicationId, sequenceNumber, capturedProfile, shared.ApplicationType,
                shared.SubmissionType, shared.SubmissionSubtype, shared.SequenceDescription, shared.ApplicantName, shared.FormType,
                shared.ApplicantContactName, shared.ApplicantContactType, shared.Telephone, shared.TelephoneNumberType, shared.Email,
                committed.WorkspaceRevision!.Value, RATools.Application.RegionalMetadata.RegionalMetadataRevisionDto.From(committed));
        }
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
        return ToDto(application, sequence) with { WorkspaceRevision = mutation.Revision,
            Regional = regionalMetadata is null ? null : RATools.Application.RegionalMetadata.RegionalMetadataRevisionDto.From(
                await regionalMetadata.GetScopedHeadAsync(applicationId, sequenceNumber, cancellationToken)) };
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
