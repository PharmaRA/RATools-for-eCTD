using RATools.Application.Abstractions.Persistence;
using RATools.Application.Standards;
using RATools.Domain.Documents;

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
        var usRegionalMetadata = new EctdUsRegionalMetadata(
            application.ApplicationNumber,
            sequenceMetadata.ApplicantName,
            sequenceMetadata.Description,
            metadata?.ApplicantContactName ?? string.Empty,
            metadata?.ApplicantContactType ?? string.Empty,
            metadata?.Telephone ?? string.Empty,
            metadata?.TelephoneNumberType ?? string.Empty,
            metadata?.Email ?? string.Empty,
            applicationMetadata.ApplicationType ?? DeriveApplicationType(application.ApplicationNumber),
            sequenceMetadata.SubmissionType,
            sequenceMetadata.SubmissionSubtype ?? string.Empty,
            sequenceMetadata.FormType);

        var placements = await placementRepository.ListBySequenceAsync(request.ApplicationId, request.SequenceNumber, cancellationToken);
        var applicationPlacements = await placementRepository.ListByApplicationAsync(request.ApplicationId, cancellationToken);
        var documents = await documentRepository.ListAsync(cancellationToken);
        var documentById = documents.ToDictionary(x => x.Id, x => x);
        var placementById = applicationPlacements.ToDictionary(x => x.Id, x => x);
        var leaves = BuildLeaves(request.ApplicationId, request.SequenceNumber, placements, placementById, documentById);
        var module1Leaves = leaves.Where(x => x.Module == "m1").ToArray();
        var ichBackboneLeaves = leaves.Where(x => x.Module is "m2" or "m3" or "m4" or "m5").ToArray();
        var publishedFiles = BuildPublishedFiles(leaves);

        return new EctdSequencePackage(
            application.Id,
            application.ApplicationNumber,
            sequence.SequenceNumber,
            profile.DisplayName,
            profile.IchEctdVersion,
            profile.UsRegionalModule1Version,
            applicationMetadata,
            sequenceMetadata,
            usRegionalMetadata,
            module1Leaves,
            ichBackboneLeaves,
            publishedFiles);
    }

    private static IReadOnlyCollection<EctdLeaf> BuildLeaves(
        Guid applicationId,
        string sequenceNumber,
        IReadOnlyCollection<DocumentPlacement> placements,
        IReadOnlyDictionary<Guid, DocumentPlacement> placementById,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
    {
        return placements
            .OrderBy(x => x.CtdSection, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Select(placement => BuildLeaf(applicationId, sequenceNumber, placement, placementById, documentById))
            .ToArray();
    }

    private static EctdLeaf BuildLeaf(
        Guid applicationId,
        string sequenceNumber,
        DocumentPlacement placement,
        IReadOnlyDictionary<Guid, DocumentPlacement> placementById,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
    {
        if (!documentById.TryGetValue(placement.DocumentId, out var document))
        {
            throw new EctdPackageDocumentNotFoundException(applicationId, sequenceNumber, placement.Id, placement.DocumentId);
        }

        var module = ClassifyModule(applicationId, sequenceNumber, placement);
        var lifecycle = BuildLifecycle(applicationId, sequenceNumber, placement, placementById, documentById);
        return new EctdLeaf(
            placement.Id,
            placement.DocumentId,
            $"leaf-{placement.Id:N}",
            placement.SequenceNumber,
            placement.CtdSection,
            module,
            MapOperation(applicationId, sequenceNumber, placement),
            placement.Title ?? document.FileName,
            PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber),
            document.FileName,
            document.MediaType,
            document.StoragePath,
            document.FileSize,
            document.Sha256,
            ResolveMd5(document),
            lifecycle);
    }

    // 包模型是 backbone 校验和的事实来源。优先使用上传时持久化的 MD5；
    // 对于回填前的存量文档（MD5 为空），在源文件存在时按文件计算补齐。
    private static string ResolveMd5(SubmissionDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.Md5))
        {
            return document.Md5;
        }

        if (!string.IsNullOrWhiteSpace(document.StoragePath) && File.Exists(document.StoragePath))
        {
            using var stream = File.OpenRead(document.StoragePath);
            using var md5 = System.Security.Cryptography.MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
        }

        return string.Empty;
    }

    private static EctdLifecycleReference? BuildLifecycle(
        Guid applicationId,
        string sequenceNumber,
        DocumentPlacement placement,
        IReadOnlyDictionary<Guid, DocumentPlacement> placementById,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
    {
        if (placement.Operation is DocumentPlacementOperation.New)
        {
            return null;
        }

        if (placement.Operation is not (DocumentPlacementOperation.Replace or DocumentPlacementOperation.Delete or DocumentPlacementOperation.Append))
        {
            return null;
        }

        if (placement.LifecycleTargetPlacementId is null)
        {
            throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, null, "target placement is missing");
        }

        if (!placementById.TryGetValue(placement.LifecycleTargetPlacementId.Value, out var targetPlacement))
        {
            throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement was not found");
        }

        if (targetPlacement.ApplicationId != applicationId)
        {
            throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement belongs to a different application");
        }

        if (!string.Equals(targetPlacement.CtdSection, placement.CtdSection, StringComparison.OrdinalIgnoreCase))
        {
            throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement is in a different CTD section");
        }

        if (CompareSequenceNumbers(targetPlacement.SequenceNumber, placement.SequenceNumber) >= 0)
        {
            throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target sequence is not earlier than current sequence");
        }

        if (!documentById.TryGetValue(targetPlacement.DocumentId, out var targetDocument))
        {
            throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target document was not found");
        }

        return new EctdLifecycleReference(
            targetPlacement.Id,
            targetPlacement.DocumentId,
            targetPlacement.SequenceNumber,
            PublishOutputNaming.BuildPublishedDocumentRelativePath(targetDocument, targetPlacement.SequenceNumber));
    }

    private static string ClassifyModule(Guid applicationId, string sequenceNumber, DocumentPlacement placement)
    {
        var module = placement.CtdSection
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant();

        return module is "m1" or "m2" or "m3" or "m4" or "m5"
            ? module
            : throw new EctdPackageInvalidSectionException(applicationId, sequenceNumber, placement.Id, placement.CtdSection);
    }

    private static string MapOperation(Guid applicationId, string sequenceNumber, DocumentPlacement placement)
    {
        return placement.Operation switch
        {
            DocumentPlacementOperation.New => "new",
            DocumentPlacementOperation.Replace => "replace",
            DocumentPlacementOperation.Delete => "delete",
            DocumentPlacementOperation.Append => "append",
            _ => throw new EctdPackageUnsupportedOperationException(applicationId, sequenceNumber, placement.Id, (int)placement.Operation)
        };
    }

    private static string DeriveApplicationType(string applicationNumber)
    {
        if (applicationNumber.StartsWith("ANDA", StringComparison.OrdinalIgnoreCase))
        {
            return "anda";
        }

        if (applicationNumber.StartsWith("NDA", StringComparison.OrdinalIgnoreCase))
        {
            return "nda";
        }

        if (applicationNumber.StartsWith("BLA", StringComparison.OrdinalIgnoreCase))
        {
            return "bla";
        }

        if (applicationNumber.StartsWith("IND", StringComparison.OrdinalIgnoreCase))
        {
            return "ind";
        }

        return string.Empty;
    }

    private static IReadOnlyCollection<EctdPublishedFile> BuildPublishedFiles(IReadOnlyCollection<EctdLeaf> leaves)
    {
        return leaves
            .GroupBy(x => x.DocumentId)
            .Select(x => x.First())
            .Select(x => new EctdPublishedFile(x.DocumentId, x.SourcePath, x.Href, x.FileName, x.FileSize, x.Sha256, x.Md5))
            .OrderBy(x => x.Href, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int CompareSequenceNumbers(string left, string right)
    {
        if (int.TryParse(left, out var leftNumber) && int.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }
}
