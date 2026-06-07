using RATools.Application.Abstractions.Persistence;
using RATools.Application.Standards;

namespace RATools.Application.Publishing.PackageModel;

public sealed class EctdPackageModelBuilder(
    IApplicationRepository applicationRepository,
    IDocumentPlacementRepository placementRepository,
    IDocumentRepository documentRepository,
    IStandardsProfileProvider standardsProfileProvider) : IEctdPackageModelBuilder
{
    public async Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await applicationRepository.GetAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            throw new EctdPackageApplicationNotFoundException(request.ApplicationId);
        }

        var sequence = application.Sequences.SingleOrDefault(x => x.SequenceNumber == request.SequenceNumber);
        if (sequence is null)
        {
            throw new EctdPackageSequenceNotFoundException(request.ApplicationId, request.SequenceNumber);
        }

        var profile = standardsProfileProvider.GetProfile(application.EctdTemplateKey);
        var metadata = sequence.PublishingMetadata;
        var applicationMetadata = new EctdApplicationMetadata(
            application.ApplicationNumber,
            application.SponsorName,
            application.Region,
            application.EctdTemplateKey,
            metadata?.ApplicationType);
        var sequenceMetadata = new EctdSequenceMetadata(
            sequence.SequenceNumber,
            metadata?.SubmissionType ?? sequence.SubmissionType,
            metadata?.SubmissionSubtype,
            metadata?.SequenceDescription ?? sequence.Description,
            metadata?.ApplicantName ?? application.SponsorName,
            metadata?.FormType);

        await placementRepository.ListBySequenceAsync(request.ApplicationId, request.SequenceNumber, cancellationToken);
        await placementRepository.ListByApplicationAsync(request.ApplicationId, cancellationToken);
        await documentRepository.ListAsync(cancellationToken);

        return new EctdSequencePackage(
            application.Id,
            application.ApplicationNumber,
            sequence.SequenceNumber,
            profile.DisplayName,
            profile.IchEctdVersion,
            profile.UsRegionalModule1Version,
            applicationMetadata,
            sequenceMetadata,
            [],
            [],
            []);
    }
}
