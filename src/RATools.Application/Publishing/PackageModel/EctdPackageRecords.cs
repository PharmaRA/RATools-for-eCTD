using RATools.Application.Standards;

namespace RATools.Application.Publishing.PackageModel;

public sealed record EctdSequencePackage(
    Guid ApplicationId,
    string ApplicationNumber,
    string SequenceNumber,
    string StandardsProfile,
    string IchEctdVersion,
    string UsRegionalModule1Version,
    BackboneXmlProfile BackboneXml,
    EctdApplicationMetadata Application,
    EctdSequenceMetadata Sequence,
    EctdUsRegionalMetadata UsRegional,
    IReadOnlyCollection<EctdLeaf> Module1Leaves,
    IReadOnlyCollection<EctdLeaf> IchBackboneLeaves,
    IReadOnlyCollection<EctdPublishedFile> PublishedFiles,
    EctdEuRegionalMetadata? EuRegional = null);

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

public sealed record EctdEuRegionalMetadata(
    string Identifier,
    string Country,
    string SubmissionType,
    string? SubmissionMode,
    string? SubmissionNumber,
    IReadOnlyCollection<string> ProcedureTrackingNumbers,
    string SubmissionUnit,
    string Applicant,
    string AgencyCode,
    string ProcedureType,
    IReadOnlyCollection<string> InventedNames,
    IReadOnlyCollection<string> Inns,
    string SequenceNumber,
    IReadOnlyCollection<string> RelatedSequences,
    string SubmissionDescription,
    string DocumentCountry,
    string ProductInformationLanguage,
    string ProductInformationType);

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
    string TargetDocumentHref)
{
    public string BuildModifiedFileHref(string backboneRelativePath)
    {
        // ICH eCTD 3.2.2: modified-file addresses the historical XML leaf by ID.
        // Each backbone is relative to its own directory within the sequence.
        var normalizedPath = backboneRelativePath.Replace('\\', '/');
        var parents = string.Concat(Enumerable.Repeat("../", normalizedPath.Count(character => character == '/') + 1));
        return $"{parents}{TargetSequenceNumber}/{normalizedPath}#leaf-{TargetPlacementId:N}";
    }
}

public sealed record EctdPublishedFile(
    Guid DocumentId,
    string SourcePath,
    string Href,
    string FileName,
    long FileSize,
    string Sha256,
    string Md5);
