using RATools.Application.Abstractions.Persistence;
using RATools.Application.Documents;
using RATools.Application.Standards;
using RATools.Domain.Documents;
using RATools.Domain.Ctd;
using RATools.Application.RegionalMetadata;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.EuRegional;
using RATools.Domain.Common;
using RATools.Domain.Standards;
using System.Text.Json;

namespace RATools.Application.Publishing.PackageModel;

public sealed class EctdPackageModelBuilder(
    IApplicationRepository applicationRepository,
    IDocumentPlacementRepository placementRepository,
    IDocumentRepository documentRepository,
    IStandardsProfileProvider standardsProfileProvider,
    IDocumentStorageBoundary documentStorageBoundary,
    ICtdNodeRepository nodeRepository,
    IWorkspaceRevisionStore revisions,
    IApplicationImportStore imports,
    RATools.Application.Ctd.NodeFileMoveGuard? moveGuard = null,
    IRegionalMetadataStore? regionalMetadataStore = null,
    StandardsSnapshotService? standardsSnapshots = null) : IStandardsBoundPackageModelBuilder
{
    public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default) => BuildCoreAsync(request, null, cancellationToken);
    public Task<EctdSequencePackage> BuildWithStandardsAsync(BuildEctdPackageRequest request, StandardsProfile profile, CancellationToken cancellationToken = default) => BuildCoreAsync(request, profile, cancellationToken);
    private async Task<EctdSequencePackage> BuildCoreAsync(BuildEctdPackageRequest request, StandardsProfile? selectedProfile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var applicationLock = await revisions.LockApplicationAsync(request.ApplicationId, cancellationToken);
        if (moveGuard is not null) await moveGuard.EnsureReadyAsync(request.ApplicationId, cancellationToken);

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

        var regional = regionalMetadataStore is null ? null : await regionalMetadataStore.GetHeadAsync(application.Id, sequence.SequenceNumber, cancellationToken);
        StandardsProfileSnapshot? regionalSnapshot = null;
        StandardsProfile profile;
        if (regional is not null)
        {
            regional.Verify();
            var region = application.EctdTemplateKey == "us-fda-ectd-3.2.2" ? "US" : application.EctdTemplateKey == "eu-ectd-3.2.2" ? "EU" : "";
            if (regional.ApplicationId != application.Id || regional.SequenceNumber != sequence.SequenceNumber || regional.RegionKey != region ||
                regional.WorkspaceRevision > sequence.WorkspaceRevision)
                throw new RegionalMetadataScopeException("The regional head does not belong to the exact application, sequence, region and workspace scope.");
            var snapshot = standardsSnapshots is null ? throw new RegionalMetadataScopeException("A regional head requires its exact archived standards snapshot.")
                : await standardsSnapshots.GetAsync(regional.StandardsSnapshotId, cancellationToken);
            regionalSnapshot = snapshot;
            if (snapshot.TemplateKey != application.EctdTemplateKey) throw new RegionalMetadataScopeException("The regional standards snapshot belongs to another template.");
            var archived = StandardsSnapshotService.Profile(snapshot);
            if (selectedProfile is not null && CanonicalJson.Digest(JsonSerializer.SerializeToElement(selectedProfile)) != CanonicalJson.Digest(JsonSerializer.SerializeToElement(archived)))
                throw new RegionalMetadataScopeException("The supplied standards profile differs from the exact regional revision's archived profile.");
            profile = archived;
        }
        else profile = selectedProfile ?? standardsProfileProvider.GetProfile(application.EctdTemplateKey);
        if (profile.TemplateKey != application.EctdTemplateKey) throw new InvalidOperationException("The selected standards snapshot does not belong to this application template.");
        var workspace = await nodeRepository.GetSequenceAsync(application.Id, sequence.SequenceNumber, cancellationToken)
            ?? throw new EctdPackageSequenceNotFoundException(application.Id, sequence.SequenceNumber);
        var backboneSources = await imports.GetBackbonesAsync(application.Id, sequence.SequenceNumber, cancellationToken);
        var euArchiveOrder = regional?.RegionKey == "EU" ? ImportedBackboneProjection.ReadEuLeafOrder(backboneSources, sequence.SequenceNumber) : null;
        var backbone = ImportedBackboneProjection.Create(profile.BackboneXml ?? throw new EctdPackageStandardsProfileException(
            request.ApplicationId, request.SequenceNumber, profile.TemplateKey, "backbone XML metadata is missing"),
            backboneSources, sequence.SequenceNumber);
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
        var documentIds = applicationPlacements.Select(x => x.DocumentId).Distinct().ToArray();
        var documents = await documentRepository.ListByIdsPreferScopedAsync(documentIds, cancellationToken);
        var documentById = documents.ToDictionary(x => x.Id, x => x);
        var placementById = applicationPlacements.ToDictionary(x => x.Id, x => x);

        foreach (var placement in placements)
        {
            if (documentById.TryGetValue(placement.DocumentId, out var document))
            {
                documentStorageBoundary.EnsureDocumentOwnedByPlacement(document, application, placement, placementById);
            }
        }

        var regionalOrder = regional is not null ? regional.DocumentContexts.Select((context, index) => (context.PlacementId, index))
            .ToDictionary(item => item.PlacementId, item => item.index) : null;
        var euWrapperOrder = euArchiveOrder is not null ? EuWrapperOrder(euArchiveOrder, placements) : null;
        var leaves = BuildLeaves(application, request.SequenceNumber, placements, placementById, documentById, profile, regionalOrder, euWrapperOrder);
        var module1Leaves = leaves.Where(x => x.Module == "m1").ToArray();
        var ichBackboneLeaves = leaves.Where(x => x.Module is "m2" or "m3" or "m4" or "m5").ToArray();
        var tree = EctdPackageNodeTree.Create(application.Id, workspace.Nodes.Select(node =>
        {
            var instance = workspace.Graph.Get(node.NodeInstanceId);
            return new EctdPackageNode(node.NodeInstanceId, instance.ParentInstanceId, instance.DefinitionKey,
                node.DefinitionVersion, node.Attributes, node.Title, node.SortOrder, node.MetadataStatus);
        }), ichBackboneLeaves, allowLegacyBinding: true);
        var publishedFiles = BuildPublishedFiles(leaves);

        var package = new EctdSequencePackage(
            application.Id,
            application.ApplicationNumber,
            sequence.SequenceNumber,
            profile.DisplayName,
            profile.IchEctdVersion,
            profile.UsRegionalModule1Version,
            backbone.Profile,
            applicationMetadata,
            sequenceMetadata,
            usRegionalMetadata,
            module1Leaves,
            tree.Leaves,
            publishedFiles,
            null,
            tree.Nodes.Concat(workspace.Nodes.Where(node => node.CtdSection == "m1").Select(node => new EctdPackageNode(
                node.NodeInstanceId, null, workspace.Graph.Get(node.NodeInstanceId).DefinitionKey, node.DefinitionVersion,
                node.Attributes, node.Title, node.SortOrder, node.MetadataStatus))).ToArray(), workspace.WorkspaceRevision, backbone.References, regional);
        if (regional?.RegionKey == "US") UsRegionalXmlWriter.ValidateTypedMetadata(package);
        if (regional?.RegionKey == "EU")
        {
            var sections = workspace.Nodes.ToDictionary(node => node.NodeInstanceId, node => node.CtdSection);
            foreach (var leaf in leaves) sections[leaf.PlacementId] = leaf.CtdSection;
            for (var index = 0; index < regional.NodeContexts.Count; index++)
                if (!sections.ContainsKey(regional.NodeContexts[index].NodeInstanceId))
                    throw new EuRegionalXmlWriterException($"nodeContexts[{index}].nodeInstanceId is outside the exact package scope.", $"nodeContexts[{index}].nodeInstanceId");
            for (var index = 0; index < regional.DocumentContexts.Count; index++)
                if (!sections.ContainsKey(regional.DocumentContexts[index].PlacementId))
                    throw new EuRegionalXmlWriterException($"documentContexts[{index}].placementId is outside the exact package scope.", $"documentContexts[{index}].placementId");
            var applicationSequences = application.Sequences.Select(item => item.SequenceNumber).ToHashSet(StringComparer.Ordinal);
            var envelopes = regional.Content!.Eu!.Envelopes;
            for (var envelopeIndex = 0; envelopeIndex < envelopes.Count; envelopeIndex++)
                for (var relatedIndex = 0; relatedIndex < envelopes[envelopeIndex].RelatedSequences.Count; relatedIndex++)
                {
                    var reference = envelopes[envelopeIndex].RelatedSequences[relatedIndex].SequenceNumber;
                    var field = $"eu.envelopes[{envelopeIndex}].relatedSequences[{relatedIndex}].sequenceNumber";
                    if (!applicationSequences.Contains(reference) || string.CompareOrdinal(reference, sequence.SequenceNumber) > 0)
                        throw new EuRegionalXmlWriterException($"{field}: related sequences must exist in this application and cannot be future sequences.", field);
                }
            var issues = RegionalMetadataValidation.Sequence(regional.Content!, regionalSnapshot!,
                applicationSequences, sequence.SequenceNumber,
                regional.NodeContexts, regional.DocumentContexts, sections, requireContextFields: false);
            var issue = issues.FirstOrDefault(item => item.Code != "RegionalQualificationPending");
            if (issue is not null) throw new EuRegionalXmlWriterException($"{issue.FieldPath}: {issue.Code}: {issue.Message}", issue.FieldPath);
            EuRegionalXmlWriter.ValidateTypedMetadata(package);
        }
        return package;
    }

    private static Dictionary<Guid, (int WrapperOrdinal, int TraversalOrdinal)> EuWrapperOrder(
        Dictionary<(string BackbonePath, string LeafId), (int WrapperOrdinal, int TraversalOrdinal)> archiveOrder,
        IReadOnlyCollection<DocumentPlacement> placements)
    {
        var result = new Dictionary<Guid, (int WrapperOrdinal, int TraversalOrdinal)>();
        foreach (var placement in placements.Where(placement => placement.ImportedSource is not null && placement.CtdSection.StartsWith("m1.", StringComparison.OrdinalIgnoreCase)))
        {
            if (!archiveOrder.TryGetValue((placement.ImportedSource!.BackboneRelativePath, placement.LeafId), out var order))
            {
                var field = $"module1Leaves[{placement.Id:D}].importedSource";
                throw new EuRegionalXmlWriterException($"{field}: requires the exact archived backbone path and leaf ID to establish original wrapper order.", field);
            }
            result[placement.Id] = order;
        }
        return result;
    }

    private EctdLeaf[] BuildLeaves(
        Domain.Applications.SubmissionApplication application,
        string sequenceNumber,
        IReadOnlyCollection<DocumentPlacement> placements,
        IReadOnlyDictionary<Guid, DocumentPlacement> placementById,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById,
        StandardsProfile profile,
        IReadOnlyDictionary<Guid, int>? regionalOrder,
        IReadOnlyDictionary<Guid, (int WrapperOrdinal, int TraversalOrdinal)>? euWrapperOrder)
    {
        return placements
            .OrderBy(x => x.CtdSection, StringComparer.OrdinalIgnoreCase)
            // EU imported wrappers carry local sibling SortOrder values. Preserve each
            // archived parent wrapper first, then permit explicit sorting inside it.
            .ThenBy(x => euWrapperOrder?.GetValueOrDefault(x.Id, (WrapperOrdinal: int.MaxValue, TraversalOrdinal: int.MaxValue)).WrapperOrdinal ?? int.MaxValue)
            .ThenBy(x => x.SortOrder)
            // Imported form wrappers have independent local sibling orders. Their persisted
            // exact placement-context order retains XML traversal order when those values tie.
            .ThenBy(x => euWrapperOrder is not null ? euWrapperOrder.GetValueOrDefault(x.Id, (WrapperOrdinal: int.MaxValue, TraversalOrdinal: int.MaxValue)).TraversalOrdinal
                : x.ImportedSource is not null && x.CtdSection.StartsWith("m1.", StringComparison.OrdinalIgnoreCase) && regionalOrder is not null
                ? regionalOrder.GetValueOrDefault(x.Id, int.MaxValue) : int.MaxValue)
            .ThenBy(x => x.Id)
            .Select(placement => BuildLeaf(application, sequenceNumber, placement, placementById, documentById, profile))
            .ToArray();
    }

    private EctdLeaf BuildLeaf(
        Domain.Applications.SubmissionApplication application,
        string sequenceNumber,
        DocumentPlacement placement,
        IReadOnlyDictionary<Guid, DocumentPlacement> placementById,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById,
        StandardsProfile profile)
    {
        if (!documentById.TryGetValue(placement.DocumentId, out var document))
        {
            throw new EctdPackageDocumentNotFoundException(application.Id, sequenceNumber, placement.Id, placement.DocumentId);
        }

        var module = ClassifyModule(application.Id, sequenceNumber, placement);
        var lifecycle = BuildLifecycle(application, sequenceNumber, placement, placementById, documentById, profile);
        return new EctdLeaf(
            placement.Id,
            placement.DocumentId,
            placement.LeafId,
            placement.SequenceNumber,
            placement.CtdSection,
            module,
            MapOperation(application.Id, sequenceNumber, placement),
            placement.Title ?? document.FileName,
            PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber),
            document.FileName,
            document.MediaType,
            document.StoragePath,
            document.FileSize,
            document.Sha256,
            placement.Operation == DocumentPlacementOperation.Delete ? string.Empty : ResolveMd5(document),
            lifecycle, placement.NodeInstanceId, placement.SortOrder, placement.ImportedSource);
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

    private EctdLifecycleReference? BuildLifecycle(
        Domain.Applications.SubmissionApplication application,
        string sequenceNumber,
        DocumentPlacement placement,
        IReadOnlyDictionary<Guid, DocumentPlacement> placementById,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById,
        StandardsProfile profile)
    {
        if (placement.Operation is DocumentPlacementOperation.New)
        {
            return null;
        }

        if (placement.Operation is not (DocumentPlacementOperation.Replace or DocumentPlacementOperation.Delete or DocumentPlacementOperation.Append))
        {
            return null;
        }

        DocumentPlacement? targetPlacement;
        if (placement.LifecycleTargetPlacementId is null)
        {
            // 与 LifecycleTargetResolver 的 SectionAndFileName 策略保持一致：验证侧
            // 允许自动匹配，发布侧必须用同一规则解析，否则又会出现"验证绿、发布红"。
            targetPlacement = ResolveAutoLifecycleTarget(application.Id, placement, placementById.Values, documentById)
                ?? throw new EctdPackageLifecycleTargetException(application.Id, sequenceNumber, placement.Id, null, "target placement is missing");
        }
        else if (!placementById.TryGetValue(placement.LifecycleTargetPlacementId.Value, out targetPlacement))
        {
            throw new EctdPackageLifecycleTargetException(application.Id, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement was not found");
        }

        if (targetPlacement.ApplicationId != application.Id)
        {
            throw new EctdPackageLifecycleTargetException(application.Id, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement belongs to a different application");
        }

        if (!string.Equals(targetPlacement.CtdSection, placement.CtdSection, StringComparison.OrdinalIgnoreCase))
        {
            throw new EctdPackageLifecycleTargetException(application.Id, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement is in a different CTD section");
        }

        if (CompareSequenceNumbers(targetPlacement.SequenceNumber, placement.SequenceNumber) >= 0)
        {
            throw new EctdPackageLifecycleTargetException(application.Id, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target sequence is not earlier than current sequence");
        }

        if (!documentById.TryGetValue(targetPlacement.DocumentId, out var targetDocument))
        {
            throw new EctdPackageLifecycleTargetException(application.Id, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target document was not found");
        }

        if (targetPlacement.NodeInstanceId != placement.NodeInstanceId || targetPlacement.Operation == DocumentPlacementOperation.Delete)
            throw new EctdPackageLifecycleTargetException(application.Id, sequenceNumber, placement.Id, targetPlacement.Id,
                "target must be a non-delete leaf in the same business node");

        documentStorageBoundary.EnsureDocumentOwnedBySequence(
            targetDocument,
            application,
            targetPlacement.SequenceNumber);

        var targetBackbone = targetPlacement.ImportedSource?.BackboneRelativePath ??
            (targetPlacement.CtdSection.StartsWith("m1", StringComparison.Ordinal) ? profile.BackboneXml!.Regional.RelativePath! : "index.xml");
        _ = new LeafAddress(application.Id, targetPlacement.SequenceNumber, targetBackbone, targetPlacement.LeafId);
        return new EctdLifecycleReference(
            targetPlacement.Id,
            targetPlacement.DocumentId,
            targetPlacement.SequenceNumber,
            PublishOutputNaming.BuildPublishedDocumentRelativePath(targetDocument, targetPlacement.SequenceNumber),
            targetPlacement.LeafId, targetBackbone);
    }

    private static DocumentPlacement? ResolveAutoLifecycleTarget(
        Guid applicationId,
        DocumentPlacement placement,
        IEnumerable<DocumentPlacement> allPlacements,
        IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
    {
        if (!documentById.TryGetValue(placement.DocumentId, out var currentDocument))
        {
            return null;
        }

        var candidates = allPlacements
            .Where(candidate => candidate.ApplicationId == applicationId)
            .Where(candidate => candidate.NodeInstanceId == placement.NodeInstanceId && candidate.Operation != DocumentPlacementOperation.Delete)
            .Where(candidate => string.Equals(candidate.CtdSection, placement.CtdSection, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => CompareSequenceNumbers(candidate.SequenceNumber, placement.SequenceNumber) < 0)
            .Where(candidate => documentById.TryGetValue(candidate.DocumentId, out var candidateDocument)
                && string.Equals(candidateDocument.FileName, currentDocument.FileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // 仅唯一匹配才自动解析；零解或多解一律回落到显式指定的错误路径。
        return candidates.Length == 1 ? candidates[0] : null;
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

    private static EctdPublishedFile[] BuildPublishedFiles(EctdLeaf[] leaves)
    {
        // delete leaf 只声明生命周期操作，不交付新文件：把被删文件复制进新序列
        // 交付包违反 eCTD 惯例（官方验证器将其视为未引用的孤儿文件）。
        return leaves
            .Where(x => !string.Equals(x.Operation, "delete", StringComparison.OrdinalIgnoreCase))
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
