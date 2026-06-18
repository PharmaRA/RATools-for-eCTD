namespace RATools.Application.Publishing.PackageModel;

public sealed record EctdSequencePackage(
    Guid ApplicationId,
    string ApplicationNumber,
    string SequenceNumber,
    string StandardsProfile,
    string IchEctdVersion,
    string UsRegionalModule1Version,
    EctdApplicationMetadata Application,
    EctdSequenceMetadata Sequence,
    EctdUsRegionalMetadata UsRegional,
    IReadOnlyCollection<EctdLeaf> Module1Leaves,
    IReadOnlyCollection<EctdLeaf> IchBackboneLeaves,
    IReadOnlyCollection<EctdPublishedFile> PublishedFiles);

public sealed record EctdApplicationMetadata(
    string ApplicationNumber,
    string SponsorName,
    string Region,
    string TemplateKey,
    string? ApplicationType);

public sealed record EctdSequenceMetadata(
    string SequenceNumber,
    string SubmissionType,
    string? SubmissionSubtype,
    string Description,
    string ApplicantName,
    string? FormType);

public sealed record EctdUsRegionalMetadata(
    string ApplicantId,
    string CompanyName,
    string SubmissionDescription,
    string ApplicantContactName,
    string ApplicantContactType,
    string Telephone,
    string TelephoneNumberType,
    string Email,
    string ApplicationType,
    string SubmissionType,
    string SubmissionSubtype,
    string? FormType);

public sealed record EctdLeaf(
    Guid PlacementId,
    Guid DocumentId,
    string LeafId,
    string SequenceNumber,
    string CtdSection,
    string Module,
    string Operation,
    string Title,
    string Href,
    string FileName,
    string MediaType,
    string SourcePath,
    long FileSize,
    string Sha256,
    string Md5,
    EctdLifecycleReference? Lifecycle);

public sealed record EctdLifecycleReference(
    Guid TargetPlacementId,
    Guid TargetDocumentId,
    string TargetSequenceNumber,
    string ModifiedFileHref);

public sealed record EctdPublishedFile(
    Guid DocumentId,
    string SourcePath,
    string Href,
    string FileName,
    long FileSize,
    string Sha256,
    string Md5);
